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
}

interface EventBus {
  subscribe(type: string, handler: Handler): () => void
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
  }

  useEffect(() => {
    const state = stateRef.current
    const source = new EventSource('/events')
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
