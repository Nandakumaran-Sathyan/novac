import { request } from './client'

export function listApplications({ search, status, page = 1, pageSize = 20 } = {}, signal) {
  const params = new URLSearchParams({ page, pageSize })
  if (search) params.set('search', search)
  if (status) params.set('status', status)
  return request(`/api/applications?${params}`, { signal })
}

export const getApplication = (id, signal) => request(`/api/applications/${id}`, { signal })

export const createApplication = (application) =>
  request('/api/applications', { method: 'POST', body: application })

export const assessApplication = (id) =>
  request(`/api/applications/${id}/assess`, { method: 'POST' })

/** Returns "Healthy" / "Unhealthy" (plain text). Unhealthy arrives as HTTP 503. */
export const getHealth = (signal) => request('/health', { signal })
