import { useEffect, useState } from 'react'
import { Check, Copy } from 'lucide-react'
import { Button } from './Button'

interface Props {
  text: string
  label?: string
  size?: 'sm' | 'md'
}

/**
 * Confirms in place rather than with a toast: the button is where the user is
 * looking, and a copy is too small an event to queue a message for.
 */
export function CopyButton({ text, label = 'Copy', size = 'sm' }: Props) {
  const [copied, setCopied] = useState(false)

  useEffect(() => {
    if (!copied) return
    const timer = setTimeout(() => setCopied(false), 2000)
    return () => clearTimeout(timer)
  }, [copied])

  return (
    <Button
      size={size}
      icon={
        copied ? (
          <Check className="size-4 text-success" aria-hidden />
        ) : (
          <Copy className="size-4" aria-hidden />
        )
      }
      onClick={async () => {
        await navigator.clipboard.writeText(text)
        setCopied(true)
      }}
    >
      {copied ? 'Copied' : label}
    </Button>
  )
}
