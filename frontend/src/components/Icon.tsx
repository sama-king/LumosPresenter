interface IconProps {
  name: string
  size?: number
  className?: string
}

export default function Icon({ name, size = 20, className = '' }: IconProps) {
  return (
    <span
      aria-hidden
      className={`material-symbols-outlined shrink-0 ${className}`}
      style={{ fontSize: size }}
    >
      {name}
    </span>
  )
}
