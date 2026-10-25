import { useEffect, useState } from 'react'
import { getHealth, listApplications } from '../api/applications'

/**
 * Day 1–3 placeholder: proves the browser → CORS → API → SQL Server path end to end.
 * The real dashboard replaces this from Day 4.
 */
export default function SystemStatusPage() {
  const [health, setHealth] = useState({ state: 'loading' })
  const [applications, setApplications] = useState({ state: 'loading' })

  useEffect(() => {
    const controller = new AbortController()

    getHealth(controller.signal)
      .then((text) => setHealth({ state: 'ok', text }))
      .catch((err) => err.name !== 'AbortError' && setHealth({ state: 'error', text: err.message }))

    listApplications({ pageSize: 5 }, controller.signal)
      .then((data) => setApplications({ state: 'ok', data }))
      .catch((err) => err.name !== 'AbortError' && setApplications({ state: 'error', text: err.message }))

    return () => controller.abort()
  }, [])

  return (
    <main className="page">
      <header>
        <h1>FinSight AI</h1>
        <p className="muted">Loan operations copilot · synthetic data only · development build</p>
      </header>

      <section className="card">
        <h2>System status</h2>
        <StatusRow label="API + SQL Server health" status={health} render={(h) => h.text} />
        <StatusRow
          label="Loan applications"
          status={applications}
          render={(a) => `${a.data.totalCount} records`}
        />
      </section>

      {applications.state === 'ok' && applications.data.items.length > 0 && (
        <section className="card">
          <h2>Most recent applications</h2>
          <ul className="plain-list">
            {applications.data.items.map((a) => (
              <li key={a.id}>
                <span>#{a.id} {a.applicantName}</span>
                <span className="muted">
                  INR {a.loanAmount.toLocaleString('en-IN')} · {a.status}
                </span>
              </li>
            ))}
          </ul>
        </section>
      )}
    </main>
  )
}

function StatusRow({ label, status, render }) {
  const text = status.state === 'loading' ? 'Checking…' : status.state === 'ok' ? render(status) : status.text
  return (
    <div className="status-row">
      <span className={`dot dot-${status.state}`} aria-hidden="true" />
      <span>{label}</span>
      <span className="muted">{text}</span>
    </div>
  )
}
