import { useCallback, useRef, useState } from 'react'
import type { ViewportRect } from '../../lib/types'
import { applyHandleDrag, type DragHandle } from './stageConfig'

interface ViewportOverlayProps {
  rect: ViewportRect
  onChange: (rect: ViewportRect) => void
}

const HANDLES: { handle: DragHandle; className: string }[] = [
  { handle: 'nw', className: 'left-0 top-0 -translate-x-1/2 -translate-y-1/2 cursor-nwse-resize' },
  { handle: 'n', className: 'left-1/2 top-0 -translate-x-1/2 -translate-y-1/2 cursor-ns-resize' },
  { handle: 'ne', className: 'right-0 top-0 translate-x-1/2 -translate-y-1/2 cursor-nesw-resize' },
  { handle: 'e', className: 'right-0 top-1/2 translate-x-1/2 -translate-y-1/2 cursor-ew-resize' },
  { handle: 'se', className: 'bottom-0 right-0 translate-x-1/2 translate-y-1/2 cursor-nwse-resize' },
  { handle: 's', className: 'bottom-0 left-1/2 -translate-x-1/2 translate-y-1/2 cursor-ns-resize' },
  { handle: 'sw', className: 'bottom-0 left-0 -translate-x-1/2 translate-y-1/2 cursor-nesw-resize' },
  { handle: 'w', className: 'left-0 top-1/2 -translate-x-1/2 -translate-y-1/2 cursor-ew-resize' },
]

/**
 * Drag layer over the preview: the dashed frame is the display's text viewport.
 * Dragging the body moves it; the 8 handles resize it. Pointer deltas are converted
 * to percent of the preview frame, so the math is resolution-independent.
 */
export default function ViewportOverlay({ rect, onChange }: ViewportOverlayProps) {
  const frameRef = useRef<HTMLDivElement>(null)
  const dragRef = useRef<{ handle: DragHandle; startRect: ViewportRect; startX: number; startY: number } | null>(null)
  const [dragging, setDragging] = useState(false)

  const beginDrag = useCallback(
    (handle: DragHandle) => (e: React.PointerEvent<HTMLElement>) => {
      e.preventDefault()
      e.stopPropagation()
      e.currentTarget.setPointerCapture(e.pointerId)
      dragRef.current = { handle, startRect: rect, startX: e.clientX, startY: e.clientY }
      setDragging(true)
    },
    [rect],
  )

  const moveDrag = useCallback(
    (e: React.PointerEvent<HTMLElement>) => {
      const drag = dragRef.current
      const frame = frameRef.current
      if (!drag || !frame) return
      const bounds = frame.getBoundingClientRect()
      const dx = ((e.clientX - drag.startX) / bounds.width) * 100
      const dy = ((e.clientY - drag.startY) / bounds.height) * 100
      onChange(applyHandleDrag(drag.startRect, drag.handle, dx, dy))
    },
    [onChange],
  )

  const endDrag = useCallback((e: React.PointerEvent<HTMLElement>) => {
    if (dragRef.current) {
      e.currentTarget.releasePointerCapture(e.pointerId)
      dragRef.current = null
      setDragging(false)
    }
  }, [])

  return (
    <div ref={frameRef} className="absolute inset-0">
      <div
        role="presentation"
        onPointerDown={beginDrag('move')}
        onPointerMove={moveDrag}
        onPointerUp={endDrag}
        onPointerCancel={endDrag}
        className="absolute cursor-move border border-dashed border-primary-fixed-dim/70 bg-primary-fixed-dim/5"
        style={{
          left: `${rect.x}%`,
          top: `${rect.y}%`,
          width: `${rect.width}%`,
          height: `${rect.height}%`,
          touchAction: 'none',
        }}
      >
        {HANDLES.map(({ handle, className }) => (
          <span
            key={handle}
            role="presentation"
            onPointerDown={beginDrag(handle)}
            onPointerMove={moveDrag}
            onPointerUp={endDrag}
            onPointerCancel={endDrag}
            className={`absolute h-2.5 w-2.5 rounded-sm bg-primary-fixed-dim ${className}`}
            style={{ touchAction: 'none' }}
          />
        ))}
        {dragging && (
          <span className="absolute left-2 top-2 rounded bg-navy-deep/90 px-2 py-1 font-mono text-mono-ui text-primary-fixed-dim">
            {rect.x},{rect.y} · {rect.width}×{rect.height}%
          </span>
        )}
      </div>
    </div>
  )
}
