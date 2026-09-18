import { useRef, useState } from 'react'
import Icon from '../../components/Icon'
import { api } from '../../lib/api'
import { mediaFileUrl } from '../../lib/media'
import type { MediaAssetDto } from '../../lib/types'
import { FieldLabel, HelpText } from './controls'

interface MediaLibraryProps {
  assets: MediaAssetDto[]
  /** id of the asset currently used as the background, if any. */
  selectedId: string | null
  onSelect: (asset: MediaAssetDto) => void
  /** Re-fetch the registry after an upload/delete. */
  onRefresh: () => Promise<void>
  onError: (message: string) => void
}

export default function MediaLibrary({
  assets,
  selectedId,
  onSelect,
  onRefresh,
  onError,
}: MediaLibraryProps) {
  const fileRef = useRef<HTMLInputElement>(null)
  const [uploading, setUploading] = useState(false)

  const handleFile = async (file: File) => {
    setUploading(true)
    try {
      const asset = await api.uploadMedia(file)
      await onRefresh()
      onSelect(asset) // auto-select the freshly uploaded asset
    } catch (err) {
      onError((err as Error).message)
    } finally {
      setUploading(false)
    }
  }

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between">
        <FieldLabel>Media Library</FieldLabel>
        <button
          type="button"
          title="Upload image or video"
          disabled={uploading}
          onClick={() => fileRef.current?.click()}
          className="text-primary transition-colors hover:text-on-surface disabled:opacity-40"
        >
          <Icon name={uploading ? 'progress_activity' : 'cloud_upload'} size={18} />
        </button>
        <input
          ref={fileRef}
          type="file"
          accept="image/*,video/*"
          className="hidden"
          onChange={e => {
            const file = e.target.files?.[0]
            if (file) void handleFile(file)
            e.target.value = '' // allow re-selecting the same file
          }}
        />
      </div>

      {assets.length === 0 ? (
        <HelpText>No media yet — upload an image or video.</HelpText>
      ) : (
        <div className="grid grid-cols-[repeat(auto-fill,minmax(6rem,1fr))] gap-3">
          {assets.map(asset => {
            const selected = asset.id === selectedId
            return (
              <button
                key={asset.id}
                type="button"
                onClick={() => onSelect(asset)}
                className="group min-w-0 space-y-2 text-left"
              >
                <div
                  className={`relative flex aspect-video items-center justify-center overflow-hidden rounded border bg-surface-container-lowest transition-all ${
                    selected
                      ? 'border-primary shadow-[0_0_12px_rgba(173,198,255,0.35)]'
                      : 'border-surface-variant group-hover:border-primary/50'
                  }`}
                >
                  {asset.kind === 'image' ? (
                    <img
                      src={mediaFileUrl(asset.id)}
                      alt={asset.title}
                      className="h-full w-full object-cover"
                    />
                  ) : (
                    <>
                      <video
                        src={mediaFileUrl(asset.id)}
                        muted
                        playsInline
                        preload="metadata"
                        className="h-full w-full object-cover"
                      />
                      <Icon
                        name="movie"
                        size={16}
                        className="absolute right-1 top-1 text-white/80 drop-shadow"
                      />
                    </>
                  )}
                </div>
                <p
                  className={`truncate font-mono text-[10px] ${
                    selected ? 'text-on-surface' : 'text-slate-muted group-hover:text-on-surface'
                  }`}
                >
                  {asset.title}
                </p>
              </button>
            )
          })}
        </div>
      )}
    </div>
  )
}
