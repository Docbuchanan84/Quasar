export type SessionVersion = { instanceId: string; revision: number }

export type Campaign = {
  id: string
  displayName: string
  updatedUtc: string
  headSequence: string
}

export type Checkpoint = {
  id: string
  sequence: string
  kind: 'named' | 'recovery'
  label: string | null
  createdUtc: string
  simulationTicks: string
}

export type CampaignSession = SessionVersion & {
  running: boolean
  dirty: boolean
  campaign: Campaign | null
  lastCheckpoint: Checkpoint | null
  rejectedCheckpoints: number
  recoveredFromCampaignId: string | null
  recoveryError: string | null
  autosaveError: string | null
}

export type CampaignStatus = CampaignSession & {
  autosave: { enabled: boolean; intervalSeconds: number }
}

export type CampaignEntry = {
  id: string
  campaign: Campaign | null
  error: string | null
}

export type CheckpointList = {
  checkpoints: Checkpoint[]
  rejectedMetadata: number
}

export type CampaignAction =
  | { route: 'new'; displayName: string }
  | { route: 'save' }
  | { route: 'save-named'; label: string }
  | { route: 'continue'; campaignId: string }
  | { route: 'load'; campaignId: string; checkpointId: string }

export class CampaignError extends Error {}

export async function campaignRequest<T>(
  route: string,
  signal = AbortSignal.timeout(5000),
  body?: object,
): Promise<T> {
  const response = await fetch(`/api/campaigns${route}`, {
    cache: 'no-store',
    signal,
    ...(body && {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body),
    }),
  })
  if (response.ok) return response.json()
  const result = await response.json().catch(() => null)
  if (!body || response.status < 500 || result?.code === 'storage_unavailable')
    throw new CampaignError(
      result?.error ?? 'Campaign data could not be accessed.',
    )
  throw new Error('Campaign operation outcome is uncertain.')
}

const saveDate = new Intl.DateTimeFormat(undefined, {
  dateStyle: 'medium',
  timeStyle: 'short',
})

export const formatSaveDate = (utc: string) => saveDate.format(new Date(utc))

export function checkpointName(checkpoint: Checkpoint) {
  return checkpoint.kind === 'named'
    ? (checkpoint.label ?? 'Manual save')
    : 'Recovery checkpoint'
}
