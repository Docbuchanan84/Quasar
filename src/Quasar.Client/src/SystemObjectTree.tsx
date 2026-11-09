import { useMemo, useState } from 'react'
import { objectColor, type LabObject } from './lab'

type Node = {
  id: string
  label: string
  object?: LabObject
  children: Node[]
}

const solarLabels: Record<string, string> = {
  'star-sun': 'Sol',
  'moon-moon': 'Luna',
  'station-oneill': 'O’Connor Station',
  'station-staryard': 'Virgin Galactic Star Yards',
}

function buildTree(objects: LabObject[]): Node[] {
  const ids = new Set(objects.map(({ id }) => id))
  const byParent = new Map<string | null, LabObject[]>()
  for (const object of objects) {
    const parentId = object.parentId && ids.has(object.parentId) ? object.parentId : null
    const siblings = byParent.get(parentId) ?? []
    siblings.push(object)
    byParent.set(parentId, siblings)
  }

  function branch(object: LabObject): Node {
    const orbitSize = (child: LabObject) => child.orbit?.semiMajorAxisKm ?? Math.hypot(
      child.position.x - object.position.x,
      child.position.y - object.position.y,
      child.position.z - object.position.z,
    )
    const directChildren = (byParent.get(object.id) ?? []).sort((a, b) =>
      orbitSize(a) - orbitSize(b) || a.id.localeCompare(b.id),
    )
    const children: Node[] = []
    const constructed: Node[] = []
    const smallBodies: Node[] = []
    for (const child of directChildren) {
      const node = branch(child)
      if (child.kind === 'station') constructed.push(node)
      else if (object.kind === 'star' && ['asteroid', 'comet', 'dwarfplanet'].includes(child.kind))
        smallBodies.push(node)
      else children.push(node)
    }
    if (constructed.length) children.push({
      id: `constructed:${object.id}`,
      label: 'Constructed',
      children: constructed.sort((a, b) => a.label.localeCompare(b.label)),
    })
    if (smallBodies.length) children.push({
      id: `small-bodies:${object.id}`,
      label: 'Small bodies',
      children: smallBodies,
    })

    return { id: object.id, label: solarLabels[object.id] ?? object.name, object, children }
  }

  return (byParent.get(null) ?? [])
    .sort((a, b) => Number(b.kind === 'star') - Number(a.kind === 'star') || a.name.localeCompare(b.name))
    .map(branch)
}

export default function SystemObjectTree({ objects, selectedId, onSelect }: {
  objects: LabObject[]
  selectedId: string
  onSelect: (id: string) => void
}) {
  const roots = useMemo(() => buildTree(objects), [objects])
  const [expansion, setExpansion] = useState<Record<string, boolean>>({})

  function renderBranch(node: Node, root = false) {
    const { id, object, label, children } = node
    const expandable = children.length > 0
    const expanded = expansion[id] ?? (root && object?.kind === 'star')
    const toggle = () => setExpansion((previous) => ({ ...previous, [id]: !expanded }))
    const chevron = <span className="object-tree-chevron" aria-hidden="true">›</span>

    return (
      <li key={id}>
        <div className="object-tree-row">
          {object ? (
            <>
              {expandable ? (
                <button
                  type="button"
                  className="object-tree-toggle"
                  aria-label={`${expanded ? 'Collapse' : 'Expand'} ${label}`}
                  aria-expanded={expanded}
                  onClick={toggle}
                >
                  {chevron}
                </button>
              ) : <span className="object-tree-toggle" aria-hidden="true" />}
              <button
                type="button"
                className="object-tree-label"
                aria-pressed={selectedId === id}
                onClick={() => onSelect(id)}
              >
                <span style={{ color: objectColor(object.kind) }} aria-hidden="true">
                  {object.kind === 'star' ? '☉' : object.kind === 'planet' ? '●' : '◆'}
                </span>
                {label}
              </button>
            </>
          ) : (
            <button type="button" className="object-tree-group" aria-expanded={expanded} onClick={toggle}>
              {chevron}
              {label}
            </button>
          )}
        </div>
        {expandable && expanded && (
          <ul role="list">{children.map((child) => renderBranch(child))}</ul>
        )}
      </li>
    )
  }

  return (
    <nav className="object-tree" aria-label="System objects">
      <ul role="list">{roots.map((node) => renderBranch(node, true))}</ul>
    </nav>
  )
}
