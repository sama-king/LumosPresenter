import { createContext, useContext, useEffect, useRef, type ReactNode } from 'react'

/**
 * One EventSource('/events') per app, shared by every page under the provider.
 * Subscribers register per event type via useServerEvent; handlers live in a
 * ref so re-renders never tear down the SSE connection, and the source itself
 * is owned by an effect (StrictMode-safe: remounting just reconnects).
 */

type Handler = (data: unknown) => void

interface StreamState {
  handlers: Map<string, Set<Handler>>
  source: EventSource | null
  attached: Set<string>
  /** Notified every time the stream (re)opens — see useServerReconnect. */
  openHandlers: Set<() => void>
}

interface EventBus {
  subscribe(type: string, handler: Handler): () => void
  subscribeOpen(handler: () => void): () => void
}

const EventStreamContext = createContext<EventBus | null>(null)

function attachListener(state: StreamState, type: string) {
  if (!state.source || state.attached.has(type)) return
  state.attached.add(type)
  state.source.addEventListener(type, e => {
    const data = JSON.parse((e as MessageEvent).data)
    state.handlers.get(type)?.forEach(h => h(data))
  })
}

export function EventStreamProvider({ children }: { children: ReactNode }) {
  const stateRef = useRef<StreamState>({
    handlers: new Map(),
    source: null,
    attached: new Set(),
    openHandlers: new Set(),
  })

  const busRef = useRef<EventBus | null>(null)
  busRef.current ??= {
    subscribe(type, handler) {
      const state = stateRef.current
      let set = state.handlers.get(type)
      if (!set) {
        set = new Set()
        state.handlers.set(type, set)
      }
      set.add(handler)
      attachListener(state, type)
      return () => set.delete(handler)
    },
    subscribeOpen(handler) {
      const state = stateRef.current
      state.openHandlers.add(handler)
      return () => state.openHandlers.delete(handler)
    },
  }

  useEffect(() => {
    const state = stateRef.current
    const source = new EventSource('/events')
    // An EventSource reconnects by itself after a drop, but it does not replay what it
    // missed — so anything driven purely by events would sit on stale data forever.
    // Every open (the first and every reconnect) is announced so subscribers can re-read
    // the state they care about.
    source.addEventListener('open', () => {
      state.openHandlers.forEach(handler => handler())
    })
    state.source = source
    state.attached = new Set()
    for (const type of state.handlers.keys()) attachListener(state, type)
    return () => {
      source.close()
      state.source = null
    }
  }, [])

  return (
    <EventStreamContext.Provider value={busRef.current}>{children}</EventStreamContext.Provider>
  )
}

export function useServerEvent<T>(type: string, handler: (data: T) => void) {
  const bus = useContext(EventStreamContext)
  if (!bus) throw new Error('useServerEvent must be used inside <EventStreamProvider>')

  const handlerRef = useRef(handler)
  handlerRef.current = handler

  useEffect(() => bus.subscribe(type, data => handlerRef.current(data as T)), [bus, type])
}

/**
 * Runs whenever the event stream opens — on mount and after every reconnect. Use it to
 * re-read anything that would otherwise be frozen at whatever arrived before the drop.
 */
export function useServerReconnect(handler: () => void) {
  const bus = useContext(EventStreamContext)
  if (!bus) throw new Error('useServerReconnect must be used inside <EventStreamProvider>')

  const handlerRef = useRef(handler)
  handlerRef.current = handler

  useEffect(() => bus.subscribeOpen(() => handlerRef.current()), [bus])
}
