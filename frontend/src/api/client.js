// Thin fetch wrapper: one place for the base URL, JSON handling and error shape.
const BASE_URL = import.meta.env.VITE_API_BASE_URL ?? 'http://localhost:5080'

/** Error carrying the HTTP status and any field errors from an RFC 7807 ProblemDetails body. */
export class ApiError extends Error {
  constructor(status, title, fieldErrors = {}) {
    super(title)
    this.name = 'ApiError'
    this.status = status
    this.fieldErrors = fieldErrors
  }
}

export async function request(path, { method = 'GET', body, signal } = {}) {
  let response
  try {
    response = await fetch(`${BASE_URL}${path}`, {
      method,
      signal,
      headers: body ? { 'Content-Type': 'application/json' } : undefined,
      body: body ? JSON.stringify(body) : undefined,
    })
  } catch (err) {
    if (err.name === 'AbortError') throw err
    throw new ApiError(0, `Cannot reach the API at ${BASE_URL}. Is the backend running?`)
  }

  const isJson = response.headers.get('content-type')?.includes('json')
  const payload = isJson ? await response.json() : await response.text()

  if (!response.ok) {
    const title = isJson ? payload.title : payload || response.statusText
    throw new ApiError(response.status, title, isJson ? payload.errors : undefined)
  }
  return payload
}
