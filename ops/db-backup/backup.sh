#!/bin/sh
# Dumps the production database, checks the dump can be read back, uploads it
# to the backup bucket, and prunes copies older than RETENTION_DAYS.
#
# Runs inside Railway's private network, so DATABASE_URL is the internal
# address and the database never needs to be reachable from outside.
set -eu

: "${DATABASE_URL:?DATABASE_URL is not set}"
: "${S3_BUCKET:?S3_BUCKET is not set}"
: "${S3_ENDPOINT:?S3_ENDPOINT is not set}"
: "${AWS_ACCESS_KEY_ID:?AWS_ACCESS_KEY_ID is not set}"
: "${AWS_SECRET_ACCESS_KEY:?AWS_SECRET_ACCESS_KEY is not set}"
: "${AWS_DEFAULT_REGION:=auto}"
export AWS_DEFAULT_REGION

RETENTION_DAYS="${RETENTION_DAYS:-30}"
PREFIX="daily"

stamp=$(date -u +%Y-%m-%dT%H%MZ)
name="career-connect-$stamp.dump"
file="/tmp/$name"

echo "Dumping database..."
# Custom format: compressed, and restorable table by table with pg_restore.
# No owners or grants, so it restores into any Postgres, not just this one.
pg_dump --format=custom --no-owner --no-privileges --file="$file" "$DATABASE_URL"

size=$(wc -c < "$file")
echo "Dumped $name ($size bytes)"

# A backup nobody has restored is a hope, not a backup. The image already
# carries a Postgres 18 server, so restore the dump into a throwaway database
# right here, and only upload it if every table comes back.
echo "Restore test..."
test_data=/tmp/restore-test
rm -rf "$test_data" && mkdir -p "$test_data" && chown postgres:postgres "$test_data"
# --no-locale: Alpine ships no system locales, and a throwaway server doesn't need one.
su-exec postgres initdb -D "$test_data" -U postgres --auth=trust --no-locale -E UTF8 > /dev/null
su-exec postgres pg_ctl -D "$test_data" -o "-c listen_addresses='' -k /tmp" -w start > /dev/null
trap 'su-exec postgres pg_ctl -D "$test_data" -m immediate stop > /dev/null 2>&1 || true' EXIT

createdb -h /tmp -U postgres restore_test
pg_restore -h /tmp -U postgres -d restore_test --no-owner --no-privileges --exit-on-error "$file"

# Row counts, restored beside live. Live can move on between the dump and the
# count (an email scan inserting a row), so a small difference is logged
# rather than treated as failure — the restore succeeding is the real test.
tables=$(psql -h /tmp -U postgres -d restore_test -Atc \
  "select format('%I', tablename) from pg_tables where schemaname = 'public' order by 1")
if [ -z "$tables" ]; then
  echo "Restore test FAILED: no tables came back." >&2
  exit 1
fi
for t in $tables; do
  restored=$(psql -h /tmp -U postgres -d restore_test -Atc "select count(*) from public.$t")
  live=$(psql "$DATABASE_URL" -Atc "select count(*) from public.$t")
  echo "  $t: $restored rows restored, $live live"
done
su-exec postgres pg_ctl -D "$test_data" -m fast stop > /dev/null
trap - EXIT
rm -rf "$test_data"
echo "Restore test passed."

aws s3 cp "$file" "s3://$S3_BUCKET/$PREFIX/$name" \
  --endpoint-url "$S3_ENDPOINT" --only-show-errors
rm -f "$file"
echo "Uploaded to $PREFIX/$name"

# Pruning. Names sort by date, so anything whose date is before the cutoff
# goes. A failure here is logged and ignored: an extra old copy is harmless,
# a failed run is not.
cutoff=$(date -u -d "@$(( $(date +%s) - RETENTION_DAYS * 86400 ))" +%Y%m%d)
aws s3 ls "s3://$S3_BUCKET/$PREFIX/" --endpoint-url "$S3_ENDPOINT" 2>/dev/null \
  | awk '{print $4}' \
  | while read -r old; do
      day=$(echo "$old" | sed -n 's/^career-connect-\([0-9]\{4\}\)-\([0-9]\{2\}\)-\([0-9]\{2\}\)T.*/\1\2\3/p')
      if [ -n "$day" ] && [ "$day" -lt "$cutoff" ]; then
        aws s3 rm "s3://$S3_BUCKET/$PREFIX/$old" --endpoint-url "$S3_ENDPOINT" --only-show-errors \
          && echo "Pruned $old" \
          || echo "Could not prune $old (ignored)"
      fi
    done

echo "Backup finished."
