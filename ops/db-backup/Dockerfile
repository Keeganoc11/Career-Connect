# The nightly database backup. pg_dump has to be at least the server's major
# version — the database is Postgres 18 — so this is pinned to match.
# Official images only: the postgres image for the client tools, and Alpine's
# own package for the S3 CLI.
FROM postgres:18-alpine

# su-exec runs the restore-test server as the postgres user; initdb refuses root.
RUN apk add --no-cache aws-cli su-exec

COPY backup.sh /usr/local/bin/backup.sh
RUN chmod +x /usr/local/bin/backup.sh

# Railway runs this on a cron schedule (railway.json). It must exit when done,
# or the next scheduled run is skipped.
CMD ["/usr/local/bin/backup.sh"]
