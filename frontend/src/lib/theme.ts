import { useState } from 'react'

export type Theme = 'dark' | 'light'

/* Persisted UI preference; index.html applies it before first paint and
   index.css maps the attribute to the light palette. Default is dark —
   the app targets dim production booths. */
const THEME_KEY = 'lumos:theme'

export function useTheme() {
  const [theme, setTheme] = useState<Theme>(() =>
    localStorage.getItem(THEME_KEY) === 'light' ? 'light' : 'dark',
  )
  const toggleTheme = () =>
    setTheme(prev => {
      const next: Theme = prev === 'dark' ? 'light' : 'dark'
      localStorage.setItem(THEME_KEY, next)
      document.documentElement.dataset.theme = next
      return next
    })
  return { theme, toggleTheme }
}
