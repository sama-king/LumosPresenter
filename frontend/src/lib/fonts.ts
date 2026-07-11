import type { FontDto } from './types'

// Matches the app's default display font so unknown slugs degrade gracefully.
const FALLBACK_FAMILY = "'Hanken Grotesk Variable', sans-serif"

const loaded = new Set<string>()

/**
 * Makes a registry font renderable. 'bundled' fonts ship in the build (imported in
 * main.tsx) so this is a no-op; future 'file' fonts (user-installed via the settings
 * page) inject their stylesheet on first use.
 */
export function ensureFontLoaded(font: FontDto): void {
  if (font.source === 'bundled' || !font.cssUrl || loaded.has(font.slug)) {
    return
  }
  const link = document.createElement('link')
  link.rel = 'stylesheet'
  link.href = font.cssUrl
  document.head.appendChild(link)
  loaded.add(font.slug)
}

export function cssFamilyFor(fonts: FontDto[], slug: string): string {
  return fonts.find(f => f.slug === slug)?.cssFamily ?? FALLBACK_FAMILY
}
