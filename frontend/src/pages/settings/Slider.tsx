import { useEffect, useRef } from 'react'

/**
 * Range input with the console's end labels.
 *
 * Dragging updates the caller's local state on every step, but the server is told only
 * once the value settles. Committing per step would fire a request per pixel of travel;
 * committing on pointer-up alone would miss both the keyboard (arrow keys never touch the
 * pointer) and a drag released off the end of the track. A short debounce covers every
 * input method with one rule.
 */
const COMMIT_DELAY_MS = 250

export default function Slider({
  min,
  max,
  value,
  onChange,
  onCommit,
  lowLabel,
  highLabel,
  ariaLabel,
}: {
  min: number
  max: number
  value: number
  onChange: (value: number) => void
  onCommit: (value: number) => void
  lowLabel: string
  highLabel: string
  ariaLabel: string
}) {
  const timer = useRef<number | undefined>(undefined)
  // Read through a ref so the debounce never calls a stale closure's handler.
  const commitRef = useRef(onCommit)
  commitRef.current = onCommit

  useEffect(() => () => globalThis.clearTimeout(timer.current), [])

  const handleChange = (next: number) => {
    onChange(next)
    globalThis.clearTimeout(timer.current)
    timer.current = globalThis.setTimeout(() => commitRef.current(next), COMMIT_DELAY_MS)
  }

  return (
    <>
      <input
        type="range"
        min={min}
        max={max}
        value={value}
        aria-label={ariaLabel}
        onChange={e => handleChange(Number(e.target.value))}
        className="settings-range w-full cursor-pointer appearance-none rounded-lg bg-surface-container-lowest"
      />
      <div className="mt-4 flex justify-between font-mono text-[10px] uppercase text-slate-muted">
        <span>{lowLabel}</span>
        <span>{highLabel}</span>
      </div>
    </>
  )
}
