import { useCallback, useState } from 'react'

/**
 * A `useState` drop-in whose value survives component unmount/remount — so a tab's UI state
 * (selections, drafts, live queue, session history) persists across in-app navigation. React
 * Router unmounts a route's element when you navigate away; without this the state would reset.
 *
 * The store is a module-level Map keyed by a stable string, so it lives for the app session and
 * resets on a full page reload (by design — we only want durability across navigation, and this
 * keeps values free of serialization constraints, unlike sessionStorage). Keys must be unique
 * per logical piece of state; namespace them per page, e.g. "scripture.chapter".
 *
 * Server-fetched lists should NOT use this — keep those in plain useState so they re-fetch on
 * mount and stay fresh. This is for durable UI/session state only.
 */
const store = new Map<string, unknown>()

export function usePersistentState<T>(key: string, initialValue: T | (() => T)): [T, React.Dispatch<React.SetStateAction<T>>] {
  const [value, setValue] = useState<T>(() => {
    if (store.has(key)) {
      return store.get(key) as T
    }
    const initial = initialValue instanceof Function ? initialValue() : initialValue
    store.set(key, initial)
    return initial
  })

  const setPersistent = useCallback<React.Dispatch<React.SetStateAction<T>>>(
    update => {
      setValue(prev => {
        const next = update instanceof Function ? (update as (p: T) => T)(prev) : update
        store.set(key, next)
        return next
      })
    },
    [key],
  )

  return [value, setPersistent]
}

/** Clears a persisted value (e.g. on explicit reset). Rarely needed — mostly for tests. */
export function clearPersistentState(key: string): void {
  store.delete(key)
}
