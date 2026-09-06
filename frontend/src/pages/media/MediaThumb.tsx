import { useState } from 'react'
import Icon from '../../components/Icon'
import { mediaLibraryFileUrl } from '../../lib/media'
import type { MediaLibraryItemDto } from '../../lib/types'

interface MediaThumbProps {
  item: MediaLibraryItemDto | undefined
  className?: string
}

/**
 * Thumbnail for one gallery item. Three states, and the broken one is the point: a linked
 * file can move or be deleted on disk at any time, so a stale path renders an error tile
 * rather than a dead image. `exists` (server-resolved) catches the common case without a
 * request; onError catches anything that goes wrong after the listing was fetched.
 *
 * Video thumbnails come from the <video> element's own first frame (preload="metadata") —
 * no server-side thumbnailing, so nothing has to be generated or cached.
 */
export default function MediaThumb({ item, className = '' }: MediaThumbProps) {
  const [failed, setFailed] = useState(false)
  const broken = !item || !item.exists || failed

  if (broken) {
    return (
      <div
        title={item ? `File not found: ${item.sourcePath}` : 'This item is no longer in the gallery'}
        className={`flex flex-col items-center justify-center gap-1 bg-surface-container-highest text-rose-error ${className}`}
      >
        <Icon name="broken_image" size={24} />
        <span className="font-mono text-[10px] uppercase tracking-widest">Missing</span>
      </div>
    )
  }

  const src = mediaLibraryFileUrl(item.id)

  if (item.kind === 'video') {
    return (
      <div className={`relative bg-black ${className}`}>
        <video
          src={src}
          muted
          preload="metadata"
          onError={() => setFailed(true)}
          className="h-full w-full object-cover"
        />
        <span className="absolute inset-0 flex items-center justify-center text-white/80">
          <Icon name="play_circle" size={28} />
        </span>
      </div>
    )
  }

  return (
    <img
      src={src}
      alt=""
      loading="lazy"
      onError={() => setFailed(true)}
      className={`bg-black object-cover ${className}`}
    />
  )
}
