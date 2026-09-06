import { useEffect, useRef, useState } from 'react'
import Icon from '../../components/Icon'

export interface MenuAction {
  label: string
  icon: string
  onSelect: () => void
  /** Renders in the error colour — used for Delete. */
  danger?: boolean
  disabled?: boolean
}

interface ContextMenuProps {
  x: number
  y: number
  actions: MenuAction[]
  onClose: () => void
}

/**
 * Right-click menu for gallery tiles and schedule rows. Positioned at the pointer and
 * nudged back inside the viewport once measured, so a right-click near the bottom edge
 * doesn't open a menu that runs off-screen.
 */
export default function ContextMenu({ x, y, actions, onClose }: ContextMenuProps) {
  const ref = useRef<HTMLDivElement>(null)
  const [position, setPosition] = useState({ left: x, top: y })

  useEffect(() => {
    const menu = ref.current
    if (!menu) return
    const { width, height } = menu.getBoundingClientRect()
    setPosition({
      left: Math.min(x, window.innerWidth - width - 8),
      top: Math.min(y, window.innerHeight - height - 8),
    })
  }, [x, y])

  // Any click outside, any scroll, or Escape dismisses. Capture phase so the dismissal
  // wins over a tile's own click handler (otherwise closing the menu also re-selects).
  useEffect(() => {
    const onPointerDown = (e: MouseEvent) => {
      if (!ref.current?.contains(e.target as Node)) onClose()
    }
    const onKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') onClose()
    }
    document.addEventListener('mousedown', onPointerDown, true)
    document.addEventListener('keydown', onKeyDown)
    window.addEventListener('resize', onClose)
    return () => {
      document.removeEventListener('mousedown', onPointerDown, true)
      document.removeEventListener('keydown', onKeyDown)
      window.removeEventListener('resize', onClose)
    }
  }, [onClose])

  return (
    <div
      ref={ref}
      role="menu"
      style={{ left: position.left, top: position.top }}
      className="fixed z-[60] min-w-52 overflow-hidden rounded-lg border border-outline-variant bg-surface-container py-1 shadow-2xl"
    >
      {actions.map(action => (
        <button
          key={action.label}
          type="button"
          role="menuitem"
          disabled={action.disabled}
          onClick={() => {
            action.onSelect()
            onClose()
          }}
          className={`flex w-full items-center gap-3 px-3 py-2 text-left text-body-md transition-colors disabled:cursor-not-allowed disabled:opacity-40 ${
            action.danger
              ? 'text-rose-error hover:bg-rose-error/10'
              : 'text-on-surface hover:bg-surface-container-high'
          }`}
        >
          <Icon name={action.icon} size={18} />
          {action.label}
        </button>
      ))}
    </div>
  )
}
