/**
 * dsh-minato-bridge: read-only status bridge.
 *
 * Writes a small JSON file once a second so an external process (dsht) can draw a status overlay.
 * It uses only two framework seams, both read-only:
 *
 * - ctx.sessions.list()                    live sessions, in creation order;
 * - ctx.sessionProjections.stateOf(...)    the token meter's per-session projections.
 *
 * Nothing here reads message text. The snapshot carries identifiers, numbers and states, so it can
 * be inspected by the user without exposing a conversation.
 */

import fs from 'node:fs'
import path from 'node:path'

/** Cordis plugin name. */
export const name = 'minato-bridge'

/** Both seams must exist; without them the fiber stays pending rather than writing nonsense. */
export const inject = ['sessions', 'sessionProjections']

const SNAPSHOT_VERSION = 2
const WRITE_INTERVAL_MS = 1000
const CONTEXT_PRESSURE_KEY = 'contextPressure'
const TOKEN_USAGE_KEY = 'tokenUsage'

/**
 * @param ctx - registrant context.
 * @param config - optional `directory` overriding where the snapshot is written.
 */
export function apply(ctx, config) {
    const directory = resolveDirectory(config)
    const target = path.join(directory, 'snapshot.json')
    let seq = 0
    let stopped = false

    const publish = () => {
        if (stopped) return
        try {
            const body = buildSnapshot(ctx, ++seq)
            writeAtomically(target, JSON.stringify(body))
        } catch (error) {
            // A bridge that throws would be uninstalled by the host; report and keep the file stale
            // instead, which the reader shows as stale rather than as current numbers.
            ctx.logger?.warn?.('minato-bridge: could not publish the snapshot: ' + describe(error))
        }
    }

    const timer = setInterval(publish, WRITE_INTERVAL_MS)
    if (typeof timer.unref === 'function') timer.unref()

    ctx.effect(() => () => {
        stopped = true
        clearInterval(timer)
    }, 'minato-bridge.timer')

    publish()
}

/** Where the snapshot goes: the same directory the reader defaults to. */
function resolveDirectory(config) {
    if (config !== null && typeof config === 'object' && typeof config.directory === 'string' && config.directory.length > 0) {
        return config.directory
    }
    const localAppData = process.env.LOCALAPPDATA ?? path.join(process.env.USERPROFILE ?? '.', 'AppData', 'Local')
    return path.join(localAppData, 'dsht')
}

/**
 * @param ctx - context carrying the session and projection seams.
 * @param seq - monotonically increasing publish counter.
 * @returns the snapshot body, ready to serialise.
 */
function buildSnapshot(ctx, seq) {
    const sessions = []
    for (const session of ctx.sessions.list()) {
        const entry = { id: String(session.id) }

        const pressure = readProjection(ctx, session, CONTEXT_PRESSURE_KEY)
        const tokens = readProjection(ctx, session, TOKEN_USAGE_KEY)

        if (pressure !== undefined && pressure !== null) {
            const used = numberOrUndefined(pressure.projectedTokens ?? pressure.pressureTokens)
            const window = numberOrUndefined(pressure.contextWindow)
            if (used !== undefined && window !== undefined && window > 0) {
                entry.ctxUsed = Math.min(1, used / window)
                entry.ctxRemainTokens = Math.max(0, window - used)
            }
        }

        if (tokens !== undefined && tokens !== null) {
            const total = tokenTotal(tokens)
            if (total !== undefined) entry.tokensTotal = total
        }

        sessions.push(entry)
    }

    return {
        v: SNAPSHOT_VERSION,
        generatedAt: new Date().toISOString().replace(/\.\d+Z$/, 'Z'),
        seq,
        sessions,
    }
}

/**
 * Reads one projection. The registry-level read is the only path available to a plugin that does
 * not own the unit; it is documented as deprecated in favour of an owning service, so the call is
 * isolated here and can be swapped when that service is exposed.
 */
function readProjection(ctx, session, key) {
    try {
        return ctx.sessionProjections.stateOf(session, key)
    } catch {
        return undefined
    }
}

/** Sum of the four disjoint usage buckets the meter records. */
function tokenTotal(usage) {
    const parts = [usage.uncachedInputTokens, usage.outputTokens, usage.cacheReadTokens, usage.cacheWriteTokens]
    let total = 0
    for (const part of parts) {
        if (typeof part !== 'number' || !Number.isFinite(part)) return undefined
        total += part
    }
    return total
}

function numberOrUndefined(value) {
    return typeof value === 'number' && Number.isFinite(value) ? value : undefined
}

function describe(error) {
    return error instanceof Error ? error.message : String(error)
}

/** Write-then-rename, so a reader never observes a half-written file. */
function writeAtomically(target, contents) {
    const directory = path.dirname(target)
    fs.mkdirSync(directory, { recursive: true })
    const temporary = target + '.' + process.pid + '.tmp'
    fs.writeFileSync(temporary, contents, 'utf8')
    fs.renameSync(temporary, target)
}
