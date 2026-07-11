import { useCallback, useEffect, useRef, useState } from 'react'
import Icon from '../../components/Icon'
import { api } from '../../lib/api'

/**
 * The address a projector/OBS browser source opens for this display. Built from the
 * machine's LAN IP (via /api/network) so it works from other devices — localhost
 * would only resolve on the operator machine. Falls back to the current origin.
 */
export default function DisplayUrlChip({ displayId }: { displayId: number }) {
  const [origin, setOrigin] = useState(window.location.origin)
  const url = `${origin}/display/${displayId}`
  const [copied, setCopied] = useState(false)
  const timer = useRef<number | undefined>(undefined)

  useEffect(() => {
    void api
      .getNetwork()
      .then(net => {
        if (net.host) setOrigin(`http://${net.host}:${net.port}`)
      })
      .catch(() => {}) // keep the current-origin fallback
  }, [])

  useEffect(() => () => window.clearTimeout(timer.current), [])

  const copy = useCallback(() => {
    void navigator.clipboard.writeText(url).then(() => {
      setCopied(true)
      window.clearTimeout(timer.current)
      timer.current = window.setTimeout(() => setCopied(false), 1500)
    })
  }, [url])

  return (
    <div className="flex items-center gap-2 rounded-lg border border-surface-variant bg-surface-container-lowest px-3 py-2">
      <Icon name="link" size={16} className="text-on-surface-variant" />
      <a
        href={url}
        target="_blank"
        rel="noreferrer"
        className="font-mono text-mono-ui text-primary hover:underline"
      >
        {url}
      </a>
      <button
        type="button"
        title="Copy display URL"
        onClick={copy}
        className="text-on-surface-variant transition-colors hover:text-primary"
      >
        <Icon name={copied ? 'check' : 'content_copy'} size={16} />
      </button>
    </div>
  )
}
