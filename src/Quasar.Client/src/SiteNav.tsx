const pages = [
  { href: '/', label: 'System Lab' },
  { href: '/torch-designer/', label: 'Torch Designer' },
] as const

export default function SiteNav({ current }: { current: (typeof pages)[number]['href'] }) {
  return (
    <nav className="site-nav" aria-label="Main navigation">
      {pages.map(({ href, label }) => (
        <a key={href} href={href} aria-current={href === current ? 'page' : undefined}>
          {label}
        </a>
      ))}
    </nav>
  )
}
