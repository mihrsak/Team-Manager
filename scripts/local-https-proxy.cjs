const fs = require('node:fs');
const http = require('node:http');
const https = require('node:https');

const [pfxPath, frontendPort, backendPort] = process.argv.slice(2);
const tls = { pfx: fs.readFileSync(pfxPath), passphrase: process.env.TEAM_MANAGER_PROXY_PFX_PASSWORD };
const publicHost = `localhost:${frontendPort}`;
const hopHeaders = ['connection', 'keep-alive', 'proxy-authenticate', 'proxy-authorization',
    'te', 'trailer', 'transfer-encoding', 'upgrade'];

function cleanHeaders(headers) {
    const cleaned = { ...headers };
    const connectionHeaders = (headers.connection || '').toLowerCase().split(',').map(name => name.trim());
    for (const name of [...hopHeaders, ...connectionHeaders]) delete cleaned[name];
    return cleaned;
}

function forward(request, response) {
    if (request.headers.host !== publicHost) {
        response.writeHead(400).end();
        return;
    }
    const headers = cleanHeaders(request.headers);
    for (const name of Object.keys(headers)) {
        if (name === 'forwarded' || name.startsWith('x-forwarded-')) delete headers[name];
    }
    headers['x-forwarded-for'] = request.socket.remoteAddress;
    headers['x-forwarded-proto'] = 'https';
    const upstream = http.request({
        hostname: '127.0.0.1',
        port: backendPort,
        path: request.url,
        method: request.method,
        headers
    }, upstreamResponse => {
        response.writeHead(upstreamResponse.statusCode, cleanHeaders(upstreamResponse.headers));
        upstreamResponse.pipe(response);
    });
    upstream.on('error', () => {
        if (!response.headersSent) response.writeHead(502);
        response.end();
    });
    response.on('close', () => upstream.destroy());
    request.pipe(upstream);
}

for (const address of ['127.0.0.1', '::1']) {
    https.createServer(tls, forward).listen(Number(frontendPort), address);
}
