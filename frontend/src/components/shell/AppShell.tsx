import { useEffect, useRef } from 'react'
import { Outlet } from 'react-router-dom'
import AppHeader from './AppHeader'
import StatusFooter from './StatusFooter'

/** Static chrome for all console pages: header + routed content + status footer. */
export default function AppShell() {
  const searchRef = useRef<HTMLInputElement>(null)

  useEffect(() => {
    const onKeyDown = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
        e.preventDefault()
        // Pages may claim the shortcut (e.g. the scripture reference input)
        // by cancelling this event; otherwise the header search takes focus.
        const claimed = !window.dispatchEvent(
          new CustomEvent('lumos:quick-search', { cancelable: true }),
        )
        if (!claimed) {
          searchRef.current?.focus()
          searchRef.current?.select()
        }
      }
      if (e.key === 'Escape' && document.activeElement === searchRef.current) {
        searchRef.current?.blur()
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [])

  return (
    <div className="flex h-screen flex-col overflow-hidden bg-app-bg">
      <AppHeader searchRef={searchRef} />
      <main className="flex min-h-0 flex-1 flex-col overflow-hidden">
        <Outlet />
      </main>
      <StatusFooter />
    </div>
  )
}
