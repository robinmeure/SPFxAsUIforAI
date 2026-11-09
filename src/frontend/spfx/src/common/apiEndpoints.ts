export interface ApiEndpoints { streamUrl: string; companionRoot?: string; }

function parseEndpoint(value: string): URL {
  const url = new URL(value);
  if ((url.protocol !== 'https:' && !(url.protocol === 'http:' && url.hostname === 'localhost'))
    || url.username || url.password || url.hash) {
    throw new Error('Use an HTTPS URL without credentials or a fragment (HTTP is allowed for localhost).');
  }
  return url;
}

export function resolveEndpoints(apiUrl: string, companionBaseUrl?: string): ApiEndpoints {
  const streamUrl = apiUrl.trim();
  const stream = parseEndpoint(streamUrl);
  let companionRoot: string | undefined;
  if (companionBaseUrl?.trim()) {
    const base = parseEndpoint(companionBaseUrl.trim());
    if (base.origin !== stream.origin || base.search) {
      throw new Error('The companion base URL must use the stream URL origin and have no query string.');
    }
    companionRoot = base.origin + base.pathname.replace(/\/+$/, '');
  } else {
    const match = /^(.*)\/api\/messages\/stream\/?$/i.exec(stream.pathname);
    if (match) companionRoot = stream.origin + match[1];
  }
  return { streamUrl, companionRoot };
}
