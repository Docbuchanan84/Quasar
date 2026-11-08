import { Component, type ReactNode } from 'react'

// A missing lazy chunk after deployment must not take the clock/orders offline.
export default class MapBoundary extends Component<
  { children: ReactNode },
  { failed: boolean }
> {
  state = { failed: false }

  static getDerivedStateFromError() {
    return { failed: true }
  }

  render() {
    if (this.state.failed)
      return (
        <div className="loading" role="alert">
          <p>
            Map display unavailable. The app may have been updated. Inspection
            and time controls remain available.
          </p>
          <button onClick={() => window.location.reload()}>
            Reload display
          </button>
        </div>
      )
    return this.props.children
  }
}
