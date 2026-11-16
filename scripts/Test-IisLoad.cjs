const assert = require('node:assert/strict');
const https = require('node:https');
const { performance } = require('node:perf_hooks');

const base = 'https://localhost:7443';
const paths = ['/Dashboard', '/Projects', '/Issues', '/Search?Query=IIS', '/Calendar', '/TimeReport'];
const durationMs = 10_000;

function request(path, options = {}) {
    return fetch(new URL(path, base), { ...options, redirect: 'manual', signal: AbortSignal.timeout(10_000) });
}

async function signIn() {
    const login = await request('/Login');
    assert.equal(login.status, 200);
    const cookie = login.headers.getSetCookie().map(value => value.split(';')[0]).join('; ');
    const token = (await login.text()).match(/name="__RequestVerificationToken"[^>]*value="([^"]+)"/)?.[1];
    assert.ok(token, 'Login antiforgery token');
    const response = await request('/Login', {
        method: 'POST',
        headers: { cookie },
        body: new URLSearchParams({ EmployeeNumber: 'admin', Password: 'admin', __RequestVerificationToken: token })
    });
    assert.equal(response.status, 302);
    await response.arrayBuffer();
    const session = response.headers.getSetCookie().find(value => value.startsWith('TeamManager.Session='));
    assert.match(session || '', /; secure\b/i);
    assert.match(session, /; httponly\b/i);
    assert.match(session, /; samesite=lax\b/i);
    return [cookie, session.split(';')[0]].filter(Boolean).join('; ');
}

async function measure(concurrency, cookie) {
    const latencies = [];
    const errors = {};
    let index = 0;
    const started = performance.now();
    async function worker() {
        while (performance.now() - started < durationMs) {
            const path = paths[index++ % paths.length];
            const sent = performance.now();
            try {
                const response = await request(path, { headers: { cookie } });
                await response.arrayBuffer();
                if (response.status !== 200) throw new Error(`HTTP ${response.status}`);
                latencies.push(performance.now() - sent);
            } catch (error) {
                errors[error.message] = (errors[error.message] || 0) + 1;
            }
        }
    }
    await Promise.all(Array.from({ length: concurrency }, worker));
    latencies.sort((a, b) => a - b);
    const percentile = fraction => Math.round(latencies[Math.ceil(latencies.length * fraction) - 1]);
    const result = {
        concurrency,
        responses: latencies.length,
        requestsPerSecond: Math.round(latencies.length * 1000 / (performance.now() - started)),
        medianMs: percentile(0.5),
        p95Ms: percentile(0.95),
        maxMs: percentile(1),
        errors
    };
    console.log(JSON.stringify(result));
    assert.ok(latencies.length >= paths.length, 'Completed one round of authenticated routes');
    assert.deepEqual(errors, {});
}

async function run() {
    for (const path of ['/health/live', '/health/ready']) {
        const health = await request(path);
        assert.equal(health.status, 200);
        assert.equal(await health.text(), 'Healthy');
    }
    const unexpectedHostStatus = await new Promise((resolve, reject) => {
        https.get(new URL('/health/live', base), {
            servername: 'localhost',
            headers: { host: 'qa-invalid.example' },
            signal: AbortSignal.timeout(10_000)
        }, response => {
            response.resume();
            resolve(response.statusCode);
        }).on('error', reject);
    });
    assert.equal(unexpectedHostStatus, 400);
    const config = await request('/appsettings.Local.json');
    assert.equal(config.status, 404);
    await config.arrayBuffer();
    const anon = await request('/Projects');
    assert.equal(anon.status, 302);
    assert.match(anon.headers.get('location'), /^https:\/\/localhost:7443\/Login\?/i);
    await anon.arrayBuffer();
    const cookie = await signIn();
    const invalidPost = await request('/Logout', { method: 'POST', headers: { cookie } });
    assert.equal(invalidPost.status, 400);
    await invalidPost.arrayBuffer();
    for (const concurrency of [1, 10, 25]) await measure(concurrency, cookie);
}

run().catch(error => { console.error(error); process.exitCode = 1; });
