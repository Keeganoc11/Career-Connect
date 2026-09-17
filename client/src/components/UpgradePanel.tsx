import { Check, Lock } from 'lucide-react'
import { PRO_AVAILABILITY, PRO_FEATURES, PRO_PRICE, PRO_PRICE_PERIOD } from '../lib/tiers'
import { Badge, Card } from './ui'

interface Props {
  /** What the person was trying to reach, so the panel answers the click they just made. */
  title: string
  description: string
  /** Trims the feature list on panels that sit inside a page rather than replacing one. */
  compact?: boolean
}

/**
 * What a Free account sees where a Pro feature would be.
 *
 * It shows the feature rather than hiding it: someone who never sees what
 * tailoring does has no reason to pay for it. There's no checkout yet, so the
 * panel says plainly how to get Pro instead of dead-ending on a button that
 * doesn't work.
 */
export function UpgradePanel({ title, description, compact = false }: Props) {
  const features = compact ? PRO_FEATURES.slice(0, 4) : PRO_FEATURES

  return (
    <Card>
      <div className="flex items-start gap-3">
        <span
          className="mt-0.5 flex size-8 shrink-0 items-center justify-center rounded-full bg-accent-soft text-accent"
          aria-hidden
        >
          <Lock className="size-4" />
        </span>
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="text-base font-semibold text-fg">{title}</h2>
            <Badge emphasis="accent">Pro</Badge>
          </div>
          <p className="mt-1 text-sm text-fg-muted">{description}</p>
        </div>
      </div>

      <ul className="mt-4 space-y-2">
        {features.map((feature) => (
          <li key={feature} className="flex items-start gap-2 text-sm text-fg">
            <Check className="mt-0.5 size-4 shrink-0 text-accent" aria-hidden />
            {feature}
          </li>
        ))}
      </ul>

      <div className="mt-5 border-t border-line pt-4">
        <p className="text-sm text-fg">
          <span className="text-base font-semibold">{PRO_PRICE}</span>{' '}
          <span className="text-fg-muted">{PRO_PRICE_PERIOD}</span>
        </p>
        <p className="mt-1 text-sm text-fg-muted">{PRO_AVAILABILITY}</p>
      </div>
    </Card>
  )
}
