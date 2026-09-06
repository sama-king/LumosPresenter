import { useEffect, useRef, useState } from 'react'
import Icon from '../../components/Icon'
import { api } from '../../lib/api'
import { isOfflineTranslation, type SearchResultDto, type Translation } from '../../lib/types'

export interface HistoryItem {
  reference: string
  book: string
  chapter: number
  verses: number[]
  snippet: string
  translation: string
  at: string // HH:MM
}

interface SearchPanelProps {
  translations: Translation[]
  currentTranslation: string
  history: HistoryItem[]
  onSwitchTranslation: (code: string) => void
  /** goLive: also push the result to the displays (Enter on an unambiguous match). */
  onPickResult: (result: SearchResultDto, options?: { goLive?: boolean }) => void
  onPickHistory: (item: HistoryItem) => void
  onError: (message: string) => void
}

/**
 * Splits a query into its leading book-name text and the trailing chapter/verse
 * part, so autocomplete only completes the book. "1 co" → { book: "1 co", rest: "" };
 * "john 3" → { book: "john", rest: " 3" }.
 */
function splitQuery(query: string): { book: string; rest: string } {
  const match = /^(\s*(?:[123]\s+)?[a-zA-Z][a-zA-Z\s]*?)(\s*\d.*)?$/.exec(query)
  if (!match) return { book: query, rest: '' }
  return { book: match[1], rest: match[2] ?? '' }
}

/** The first book whose name starts with the typed book text (case-insensitive). */
function completeBook(bookText: string, books: string[]): string | null {
  const trimmed = bookText.trim()
  if (trimmed.length < 2) return null
  const lower = trimmed.toLowerCase()
  const hit = books.find(b => b.toLowerCase().startsWith(lower))
  return hit && hit.toLowerCase() !== lower ? hit : null
}

export default function SearchPanel({
  translations,
  currentTranslation,
  history,
  onSwitchTranslation,
  onPickResult,
  onPickHistory,
  onError,
}: SearchPanelProps) {
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<SearchResultDto[]>([])
  const [searched, setSearched] = useState(false)
  const [busy, setBusy] = useState(false)
  const [books, setBooks] = useState<string[]>([])
  const inputRef = useRef<HTMLInputElement>(null)

  const offline = translations.filter(isOfflineTranslation)
  const online = translations.filter(t => !isOfflineTranslation(t))


  useEffect(() => {
    void api
      .getBooks()
      .then(data => setBooks(data.books))
      .catch(() => {})
  }, [])

  // Claim the global ⌘K shortcut while this panel is mounted.
  useEffect(() => {
    const onQuickSearch = (e: Event) => {
      e.preventDefault()
      inputRef.current?.focus()
      inputRef.current?.select()
    }
    window.addEventListener('lumos:quick-search', onQuickSearch)
    return () => window.removeEventListener('lumos:quick-search', onQuickSearch)
  }, [])

  const { book: bookPart, rest } = splitQuery(query)
  const bookCompletion = completeBook(bookPart, books)
  // The full text the ghost suggests (typed book → canonical book, preserving rest).
  const completedQuery = bookCompletion
    ? bookPart.replace(/\S.*/, '') + bookCompletion + rest
    : null

  const acceptCompletion = () => {
    if (!completedQuery) return false
    setQuery(completedQuery.trimStart())
    return true
  }

  const search = (e: React.FormEvent) => {
    e.preventDefault()
    const q = query.trim()
    if (!q || busy) return
    setBusy(true)
    api
      .searchScripture(q)
      .then(response => {
        setResults(response.results)
        setSearched(true)
        // Unambiguous: preview AND push live. Ambiguous: preview the top hit only.
        if (response.results.length === 1) onPickResult(response.results[0], { goLive: true })
        else if (response.results.length > 1) onPickResult(response.results[0])
      })
      .catch((err: Error) => onError(err.message))
      .finally(() => setBusy(false))
  }

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    // Tab accepts the ghost book completion instead of moving focus.
    if (e.key === 'Tab' && completedQuery) {
      e.preventDefault()
      acceptCompletion()
    }
  }

  return (
    <aside className="flex w-[300px] shrink-0 flex-col border-r border-outline-variant bg-surface-container-low">
      <div className="border-b border-outline-variant p-4">
        <div className="mb-3 flex items-center gap-2 text-on-surface">
          <Icon name="search" size={18} />
          <h2 className="font-mono text-status-label uppercase">Search</h2>
        </div>
        <form onSubmit={search} className="mb-3">
          <div className="relative">
            {/* Ghost completion: typed text is transparent, suffix shows the rest of the book name. */}
            {completedQuery && (
              <div
                aria-hidden
                className="pointer-events-none absolute inset-0 overflow-hidden whitespace-pre px-3 py-2.5 text-body-md text-slate-muted"
              >
                <span className="invisible">{query}</span>
                <span>{completedQuery.trimStart().slice(query.length)}</span>
              </div>
            )}
            <input
              ref={inputRef}
              value={query}
              onChange={e => setQuery(e.target.value)}
              onKeyDown={onKeyDown}
              placeholder="Type a reference..."
              spellCheck={false}
              autoComplete="off"
              className="relative w-full rounded border border-outline-variant bg-surface-container-lowest px-3 py-2.5 text-body-md placeholder:text-slate-muted focus:border-primary focus:outline-none"
            />
          </div>
          {completedQuery && (
            <p className="mt-1 font-mono text-[10px] text-slate-muted">
              Tab to complete “{completedQuery.trim()}”
            </p>
          )}
        </form>
        <div className="relative">
          <select
            value={currentTranslation}
            onChange={e => onSwitchTranslation(e.target.value)}
            className="w-full appearance-none rounded border border-outline-variant bg-surface-container-lowest px-3 py-2.5 text-body-md focus:border-primary focus:outline-none"
          >
            {/* Grouped so the offline-safe set is distinguishable when the list is open;
                the closed select stays a plain label, with no source badge. */}
            <optgroup label="Offline">
              {offline.map(t => (
                <option key={t.id} value={t.id}>
                  {t.id} — {t.name}
                </option>
              ))}
            </optgroup>
            {online.length > 0 && (
              <optgroup label="Online">
                {online.map(t => (
                  <option key={t.id} value={t.id}>
                    {t.id} — {t.name}
                  </option>
                ))}
              </optgroup>
            )}
          </select>
          <Icon
            name="expand_more"
            className="pointer-events-none absolute right-3 top-1/2 -translate-y-1/2 text-on-surface-variant"
          />
        </div>

        {searched && (
          <div className="mt-3">
            {results.length === 0 ? (
              <p className="text-mono-ui font-mono text-slate-muted">No reference recognized.</p>
            ) : (
              <ul className="flex flex-col gap-1">
                {results.map((r, i) => (
                  <li key={i}>
                    <button
                      type="button"
                      onClick={() => onPickResult(r)}
                      className="w-full rounded border border-outline-variant bg-surface-container px-3 py-2 text-left transition-colors hover:border-primary/50"
                    >
                      <span className="flex items-baseline justify-between">
                        <span className="font-mono text-status-label text-primary">{r.display}</span>
                        <span className="font-mono text-[10px] text-slate-muted">
                          {Math.round(r.confidence * 100)}%
                        </span>
                      </span>
                      {r.verses[0] && (
                        <span className="mt-1 line-clamp-1 block text-mono-ui italic text-on-surface-variant">
                          “{r.verses[0].text}”
                        </span>
                      )}
                    </button>
                  </li>
                ))}
              </ul>
            )}
          </div>
        )}
      </div>

      <div className="flex min-h-0 flex-1 flex-col p-4">
        <h3 className="mb-3 font-mono text-mono-ui uppercase tracking-widest text-slate-muted">
          Recent History
        </h3>
        <div className="panel-scroll min-h-0 flex-1 overflow-y-auto">
          {history.length === 0 ? (
            <p className="text-mono-ui font-mono italic text-slate-muted">
              Verses shown live appear here.
            </p>
          ) : (
            <ul className="flex flex-col gap-1">
              {history.map((item, i) => (
                <li key={i}>
                  <button
                    type="button"
                    onClick={() => onPickHistory(item)}
                    className="w-full rounded px-2 py-2 text-left transition-colors hover:bg-surface-container-high"
                  >
                    <span className="flex items-baseline justify-between">
                      <span className="text-body-md font-semibold text-on-surface">
                        {item.reference}
                      </span>
                      <span className="font-mono text-mono-ui text-slate-muted">{item.at}</span>
                    </span>
                    <span className="line-clamp-1 block text-mono-ui italic text-on-surface-variant">
                      “{item.snippet}”
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      </div>
    </aside>
  )
}
