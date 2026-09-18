import Icon from '../../components/Icon'
import type {
  DisplayConfig,
  DisplayDto,
  FontDto,
  MediaAssetDto,
  MediaDisplayConfig,
  ReferenceConfig,
  TextDisplayConfig,
} from '../../lib/types'
import ConfigRail, { type RailSection } from './ConfigRail'
import {
  ColorField,
  FieldLabel,
  HelpText,
  SectionLabel,
  SegmentedIconToggle,
  SelectField,
  SliderField,
} from './controls'
import MediaLibrary from './MediaLibrary'
import { WEIGHT_LABELS, type SectionId } from './stageConfig'

interface ConfigPanelProps {
  display: DisplayDto
  displays: DisplayDto[]
  draft: DisplayConfig
  fonts: FontDto[]
  media: MediaAssetDto[]
  /** Active rail section; lives in StagePage so the preview tracks the edited content type. */
  active: SectionId
  onSelect: (id: SectionId) => void
  onChange: (patch: Partial<DisplayConfig>) => void
  onChangeSource: (followsDisplayId: number | null) => void
  onMediaChange: () => Promise<void>
  onError: (message: string) => void
}

function weightOptions(fonts: FontDto[], slug: string) {
  const weights = fonts.find(f => f.slug === slug)?.weights ?? [400, 700]
  return weights.map(w => ({ value: String(w), label: `${WEIGHT_LABELS[w] ?? w} (${w})` }))
}

/** Keeps the weight valid when switching to a font that doesn't offer it (e.g. Bebas Neue). */
function clampWeight(fonts: FontDto[], slug: string, weight: number): number {
  const weights = fonts.find(f => f.slug === slug)?.weights ?? [400, 700]
  if (weights.includes(weight)) return weight
  return weights.reduce((best, w) => (Math.abs(w - weight) < Math.abs(best - weight) ? w : best))
}

interface TextSectionProps {
  text: TextDisplayConfig
  fonts: FontDto[]
  onChange: (patch: Partial<TextDisplayConfig>) => void
}

/** Typography + alignment/position sections, shared by the scripture and songs tabs. */
function TextSections({ text, fonts, onChange }: TextSectionProps) {
  const fontOptions = fonts.map(f => ({ value: f.slug, label: f.name }))
  return (
    <>
      <section className="space-y-4 rounded-xl border border-surface-variant/50 bg-surface-container p-4">
        <SectionLabel icon="text_fields">Typography</SectionLabel>
        <div className="grid grid-cols-2 gap-3">
          <SelectField
            label="Font family"
            value={text.fontSlug}
            options={fontOptions}
            onChange={slug =>
              onChange({ fontSlug: slug, fontWeight: clampWeight(fonts, slug, text.fontWeight) })
            }
          />
          <SelectField
            label="Font weight"
            value={String(text.fontWeight)}
            options={weightOptions(fonts, text.fontSlug)}
            onChange={value => onChange({ fontWeight: Number(value) })}
          />
        </div>
        <SliderField
          label="Max font size"
          value={text.fontSizePx}
          min={24}
          max={160}
          layout="row"
          onChange={fontSizePx => onChange({ fontSizePx })}
        />
        <ColorField
          label="Font color"
          value={text.textColor}
          layout="row"
          onChange={textColor => onChange({ textColor })}
        />
        <HelpText>Text auto-fits the window; long passages shrink below this cap.</HelpText>
      </section>

      <section className="space-y-4 rounded-xl border border-surface-variant/50 bg-surface-container p-4">
        <SectionLabel icon="format_align_center">Alignment &amp; Position</SectionLabel>
        <div className="grid grid-cols-2 gap-4">
          <SegmentedIconToggle
            label="Horizontal"
            value={text.horizontalAlign}
            options={[
              { value: 'left', icon: 'format_align_left', title: 'Left' },
              { value: 'center', icon: 'format_align_center', title: 'Center' },
              { value: 'right', icon: 'format_align_right', title: 'Right' },
            ]}
            onChange={horizontalAlign => onChange({ horizontalAlign })}
          />
          <SegmentedIconToggle
            label="Vertical"
            value={text.verticalAlign}
            options={[
              { value: 'top', icon: 'vertical_align_top', title: 'Top' },
              { value: 'middle', icon: 'vertical_align_center', title: 'Middle' },
              { value: 'bottom', icon: 'vertical_align_bottom', title: 'Bottom' },
            ]}
            onChange={verticalAlign => onChange({ verticalAlign })}
          />
        </div>
        <div className="space-y-2">
          <FieldLabel>Padding (px)</FieldLabel>
          <div className="grid grid-cols-4 gap-2">
            {(['top', 'right', 'bottom', 'left'] as const).map(side => (
              <label key={side} className="block">
                <span className="mb-1 block text-center font-mono text-mono-ui capitalize text-slate-muted">
                  {side}
                </span>
                <input
                  type="number"
                  min={0}
                  max={400}
                  value={text.padding[side]}
                  onChange={e =>
                    onChange({
                      padding: {
                        ...text.padding,
                        [side]: Math.min(Math.max(Number(e.target.value) || 0, 0), 400),
                      },
                    })
                  }
                  className="w-full rounded-lg border border-surface-variant bg-surface-container-lowest px-1 py-2 text-center font-mono text-status-label text-on-surface focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
                />
              </label>
            ))}
          </div>
        </div>
        <HelpText>Drag the frame in the preview to move or resize this window.</HelpText>
      </section>
    </>
  )
}

interface BackgroundSectionProps {
  text: TextDisplayConfig
  media: MediaAssetDto[]
  onChange: (patch: Partial<TextDisplayConfig>) => void
  onMediaChange: () => Promise<void>
  onError: (message: string) => void
}

/** Window background (solid / image / motion) section, shared by the scripture and songs tabs. */
function BackgroundSection({ text, media, onChange, onMediaChange, onError }: BackgroundSectionProps) {
  const isMedia = text.background.type !== 'solid'
  const selectSolid = () =>
    onChange({ background: { ...text.background, type: 'solid', assetId: null } })
  const selectMedia = () => {
    // Restore the kind of the referenced asset if there is one; otherwise start empty.
    const current = media.find(m => m.id === text.background.assetId)
    onChange({ background: { ...text.background, type: current?.kind ?? 'image' } })
  }
  return (
    <section className="space-y-4 rounded-xl border border-surface-variant/50 bg-surface-container p-4">
      <SectionLabel icon="wallpaper">Background</SectionLabel>
      <div className="flex rounded-lg border border-surface-variant bg-surface-container-lowest p-1">
        <button
          type="button"
          onClick={selectSolid}
          className={
            isMedia
              ? 'flex-1 rounded-md py-2 font-mono text-status-label text-on-surface-variant transition-colors hover:text-on-surface'
              : 'flex-1 rounded-md bg-primary py-2 font-mono text-status-label text-on-primary'
          }
        >
          Solid Color
        </button>
        <button
          type="button"
          onClick={selectMedia}
          className={
            isMedia
              ? 'flex-1 rounded-md bg-primary py-2 font-mono text-status-label text-on-primary'
              : 'flex-1 rounded-md py-2 font-mono text-status-label text-on-surface-variant transition-colors hover:text-on-surface'
          }
        >
          Image / Motion
        </button>
      </div>
      {isMedia ? (
        <MediaLibrary
          assets={media}
          selectedId={text.background.assetId ?? null}
          onSelect={asset =>
            onChange({ background: { ...text.background, type: asset.kind, assetId: asset.id } })
          }
          onRefresh={onMediaChange}
          onError={onError}
        />
      ) : (
        <>
          <ColorField
            label="Window background"
            value={text.background.color}
            layout="row"
            onChange={color => onChange({ background: { ...text.background, color } })}
          />
          <HelpText>
            Colors only the text window — everything outside stays transparent (alpha in
            OBS; dark in a plain browser).
          </HelpText>
        </>
      )}
    </section>
  )
}

export default function ConfigPanel({
  display,
  displays,
  draft,
  fonts,
  media,
  active,
  onSelect,
  onChange,
  onChangeSource,
  onMediaChange,
  onError,
}: ConfigPanelProps) {
  const fontOptions = fonts.map(f => ({ value: f.slug, label: f.name }))
  const following = display.followsDisplayId !== null
  const source = displays.find(d => d.id === display.followsDisplayId)
  // A display that has followers must stay custom (no chains).
  const hasFollowers = displays.some(d => d.followsDisplayId === display.id)
  const sourceCandidates = displays.filter(d => d.id !== display.id && d.followsDisplayId === null)

  const updateText = (type: 'scripture' | 'songs') => (patch: Partial<TextDisplayConfig>) =>
    onChange({ [type]: { ...draft[type], text: { ...draft[type].text, ...patch } } })
  const updateReference = (patch: Partial<ReferenceConfig>) =>
    onChange({ scripture: { ...draft.scripture, reference: { ...draft.scripture.reference, ...patch } } })
  const updateMedia = (patch: Partial<MediaDisplayConfig>) =>
    onChange({ media: { ...draft.media, ...patch } })

  const sections: RailSection[] = [
    { id: 'scripture', icon: 'menu_book', label: 'Scripture' },
    { id: 'songs', icon: 'music_note', label: 'Songs' },
    { id: 'media', icon: 'image', label: 'Media' },
  ]
  const footer: RailSection[] = [{ id: 'source', icon: 'link', label: 'Settings Source' }]

  const reference = draft.scripture.reference
  const referenceSection = (
    <section className="space-y-4 rounded-xl border border-surface-variant/50 bg-surface-container p-4">
      <div className="flex items-center justify-between">
        <SectionLabel icon="menu_book">Scripture Reference</SectionLabel>
        <button
          type="button"
          role="switch"
          aria-checked={reference.show}
          onClick={() => updateReference({ show: !reference.show })}
          className={
            reference.show
              ? 'relative h-5 w-9 rounded-full bg-emerald-live transition-colors'
              : 'relative h-5 w-9 rounded-full bg-surface-variant transition-colors'
          }
        >
          <span
            className={
              reference.show
                ? 'absolute left-4.5 top-0.5 h-4 w-4 rounded-full bg-white transition-all'
                : 'absolute left-0.5 top-0.5 h-4 w-4 rounded-full bg-white transition-all'
            }
          />
        </button>
      </div>
      {reference.show ? (
        <>
          <SelectField
            label="Position"
            value={reference.position}
            options={[
              { value: 'above-left', label: 'Above left' },
              { value: 'above-center', label: 'Above center' },
              { value: 'above-right', label: 'Above right' },
              { value: 'below-left', label: 'Below left' },
              { value: 'below-center', label: 'Below center' },
              { value: 'below-right', label: 'Below right' },
            ]}
            layout="row"
            onChange={value => updateReference({ position: value as ReferenceConfig['position'] })}
          />
          <div className="grid grid-cols-2 gap-3">
            <SelectField
              label="Font family"
              value={reference.fontSlug}
              options={fontOptions}
              onChange={slug =>
                updateReference({
                  fontSlug: slug,
                  fontWeight: clampWeight(fonts, slug, reference.fontWeight),
                })
              }
            />
            <SelectField
              label="Font weight"
              value={String(reference.fontWeight)}
              options={weightOptions(fonts, reference.fontSlug)}
              onChange={value => updateReference({ fontWeight: Number(value) })}
            />
          </div>
          <SliderField
            label="Font size"
            value={reference.fontSizePx}
            min={12}
            max={96}
            layout="row"
            onChange={fontSizePx => updateReference({ fontSizePx })}
          />
          <ColorField
            label="Font color"
            value={reference.color}
            layout="row"
            onChange={color => updateReference({ color })}
          />
        </>
      ) : (
        <HelpText>The reference line (e.g. “John 3:16 · KJV”) is hidden on this display.</HelpText>
      )}
    </section>
  )

  const mediaSection = (
    <section className="space-y-4 rounded-xl border border-surface-variant/50 bg-surface-container p-4">
      <SectionLabel icon="image">Media Window</SectionLabel>
      <SelectField
        label="Fit"
        value={draft.media.fit}
        options={[
          { value: 'cover', label: 'Fill window (crop)' },
          { value: 'contain', label: 'Fit inside (letterbox)' },
        ]}
        layout="row"
        onChange={value => updateMedia({ fit: value as MediaDisplayConfig['fit'] })}
      />
      <ColorField
        label="Letterbox / idle fill"
        value={draft.media.backgroundColor}
        layout="row"
        onChange={backgroundColor => updateMedia({ backgroundColor })}
      />
      <div className="flex items-center justify-between">
        <SectionLabel icon="volume_up">Video Sound</SectionLabel>
        <button
          type="button"
          role="switch"
          aria-checked={draft.media.audio}
          onClick={() => updateMedia({ audio: !draft.media.audio })}
          className={
            draft.media.audio
              ? 'relative h-5 w-9 rounded-full bg-emerald-live transition-colors'
              : 'relative h-5 w-9 rounded-full bg-surface-variant transition-colors'
          }
        >
          <span
            className={
              draft.media.audio
                ? 'absolute left-4.5 top-0.5 h-4 w-4 rounded-full bg-white transition-all'
                : 'absolute left-0.5 top-0.5 h-4 w-4 rounded-full bg-white transition-all'
            }
          />
        </button>
      </div>
      <HelpText>
        {draft.media.audio
          ? 'Video sound plays out of this display, at the level set in the live panel. Turn it off on any display that is not the one wired to the speakers — two unmuted displays play the clip twice, slightly apart.'
          : 'This display is silent. Video still plays; the sound comes from whichever display has this turned on.'}
      </HelpText>
      <HelpText>
        Images and videos pushed live render inside this window. Drag the frame in the
        preview to move or resize it.
      </HelpText>
    </section>
  )

  const sourceSection = (
    <section className="space-y-4 rounded-xl border border-surface-variant/50 bg-surface-container p-4">
      <SectionLabel icon="link">Settings Source</SectionLabel>
      <SelectField
        label="Use settings of"
        value={following ? String(display.followsDisplayId) : 'custom'}
        options={[
          { value: 'custom', label: 'Custom (own settings)' },
          ...sourceCandidates.map(d => ({ value: String(d.id), label: d.name })),
        ]}
        onChange={value => onChangeSource(value === 'custom' ? null : Number(value))}
      />
      {hasFollowers && !following && (
        <HelpText>Other displays use this display's settings.</HelpText>
      )}
    </section>
  )

  return (
    <aside className="flex min-h-0 flex-1 border-r border-surface-variant bg-surface-container-low">
      <ConfigRail
        sections={sections}
        footer={footer}
        active={active}
        onSelect={id => onSelect(id as SectionId)}
      />
      <div className="panel-scroll min-h-0 flex-1 overflow-y-auto">
        <div className="space-y-4 p-4">
          {/* Always explain why controls are locked, whichever section is open. */}
          {following && (
            <p className="flex items-start gap-2 rounded-lg border border-primary/20 bg-primary/10 p-3 text-status-label text-primary">
              <Icon name="sync_lock" size={18} className="mt-0.5" />
              Mirroring {source?.name ?? 'another display'} — edit settings there, or switch back
              to Custom to detach with the current look.
            </p>
          )}
          {active === 'source' ? (
            sourceSection
          ) : (
            <fieldset
              disabled={following}
              className={following ? 'pointer-events-none space-y-4 opacity-40' : 'space-y-4'}
            >
              {active === 'scripture' && (
                <>
                  <TextSections
                    text={draft.scripture.text}
                    fonts={fonts}
                    onChange={updateText('scripture')}
                  />
                  {referenceSection}
                  <BackgroundSection
                    text={draft.scripture.text}
                    media={media}
                    onChange={updateText('scripture')}
                    onMediaChange={onMediaChange}
                    onError={onError}
                  />
                </>
              )}
              {active === 'songs' && (
                <>
                  <TextSections text={draft.songs.text} fonts={fonts} onChange={updateText('songs')} />
                  <BackgroundSection
                    text={draft.songs.text}
                    media={media}
                    onChange={updateText('songs')}
                    onMediaChange={onMediaChange}
                    onError={onError}
                  />
                </>
              )}
              {active === 'media' && mediaSection}
            </fieldset>
          )}
        </div>
      </div>
    </aside>
  )
}
