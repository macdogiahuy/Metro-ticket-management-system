"""Rate limited, cached HTTP GETs; no authentication or browser impersonation."""
import hashlib
import json
import time
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from urllib.parse import urlparse
from urllib.robotparser import RobotFileParser

BASE = 'https://metrohanoi.vn/'
USER_AGENT = 'MetroDomainResearch/1.0 (public reference data; read-only)'

class SameOriginRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        parsed=urlparse(newurl)
        if parsed.scheme!='https' or parsed.hostname!='metrohanoi.vn':
            raise urllib.error.URLError('Redirect outside the permitted HTTPS origin')
        return super().redirect_request(request,fp,code,msg,headers,newurl)

def now():
    return datetime.now(timezone.utc).isoformat()

class PublicClient:
    def __init__(self, root=Path('output'), delay=1.5):
        self.root = Path(root)
        self.cache = self.root / 'evidence'
        self.cache.mkdir(parents=True, exist_ok=True)
        self.delay = delay
        self.last_request = 0
        self.robot = None
        self.events = []
        self.stop = False
        self.opener = urllib.request.build_opener(SameOriginRedirect())

    def get(self, url, purpose='public_resource', check_robots=True, *, method='GET', payload=None, headers=None, fresh=False, redact_config=False):
        if urlparse(url).scheme != 'https' or urlparse(url).hostname != 'metrohanoi.vn':
            raise ValueError('Only HTTPS resources on metrohanoi.vn are allowed')
        request_identity = json.dumps([url, method, payload, bool(headers)], sort_keys=True)
        key = hashlib.sha256(request_identity.encode()).hexdigest()[:24]
        meta_path = self.cache / (key + '.json')
        body_path = self.cache / (key + '.body')
        if self.stop:
            raise RuntimeError('Crawl halted by rate limit or access restriction')
        if not fresh and meta_path.exists() and body_path.exists():
            meta = json.loads(meta_path.read_text(encoding='utf-8'))
            self.events.append({**meta, 'cache_hit': True, 'purpose': purpose})
            if meta.get('status') in (401,403,429):
                self.stop=True
            return meta, body_path.read_bytes()
        if check_robots and self.robot and not self.robot.can_fetch(USER_AGENT, url):
            meta = {'url': url, 'status': None, 'error': 'robots_disallowed', 'retrieved_at': now(), 'purpose': purpose}
            self.events.append(meta)
            return meta, b''
        time.sleep(max(0, self.delay - (time.monotonic() - self.last_request)))
        self.last_request = time.monotonic()
        request_headers = {'User-Agent': USER_AGENT, 'Accept': '*/*', **(headers or {})}
        data = None if payload is None else json.dumps(payload, ensure_ascii=False).encode('utf-8')
        if data is not None:
            request_headers['Content-Type'] = 'application/json'
        request = urllib.request.Request(url, headers=request_headers, data=data, method=method)
        try:
            with self.opener.open(request, timeout=35) as response:
                # Follow only same-origin public redirects.
                if urlparse(response.url).hostname != 'metrohanoi.vn':
                    raise ValueError('Redirect leaves allowed origin')
                body = response.read(20_000_001)
                if len(body) > 20_000_000:
                    raise ValueError('Resource exceeds 20 MB limit')
                meta = {'url': url, 'final_url': response.url, 'status': response.status,
                        'content_type': response.headers.get('Content-Type'),
                        'retrieved_at': now(), 'bytes': len(body), 'sha256': hashlib.sha256(body).hexdigest(),
                        'purpose': purpose, 'method': method, 'request_payload': payload,
                        'public_client_header_used': bool(headers),
                        'evidence_path': str(body_path).replace('\\', '/')}
        except urllib.error.HTTPError as exc:
            body = b''
            meta = {'url': url, 'status': exc.code, 'retrieved_at': now(), 'error': str(exc), 'purpose': purpose}
            if exc.code in (401, 403, 429):
                self.stop = True
        except (urllib.error.URLError, TimeoutError) as exc:
            body = b''
            meta = {'url': url, 'status': None, 'retrieved_at': now(), 'error': str(exc), 'purpose': purpose}
        stored_body = body
        if redact_config and body:
            config = json.loads(body)
            stored_body = json.dumps({'environment': config.get('environment'), 'api': {'baseUrl': config.get('api', {}).get('baseUrl'), 'apiKey': '[REDACTED]'}, 'note': 'Client and map keys omitted from exported evidence.'}, ensure_ascii=False, indent=2).encode('utf-8')
            meta['evidence_redacted'] = True
        body_path.write_bytes(stored_body)
        meta['evidence_sha256']=hashlib.sha256(stored_body).hexdigest()
        meta_path.write_text(json.dumps(meta, ensure_ascii=False, indent=2), encoding='utf-8')
        self.events.append(meta)
        print(f"{method} {meta['status']} {url} ({len(body)} bytes)", flush=True)
        return meta, body

    def robots(self):
        meta, body = self.get(BASE + 'robots.txt', 'robots', False)
        text = body.decode('utf-8', errors='replace')
        if meta['status'] in (401, 403, 429) or meta['status'] is None or (meta['status'] and meta['status'] >= 500):
            self.stop = True
            return {'status': 'unavailable_stop', 'source_url': BASE + 'robots.txt', **meta}
        if '<html' in text.lower() or '<!doctype' in text.lower():
            return {'status': 'spa_fallback_no_directives', 'source_url': BASE + 'robots.txt',
                    'description': 'HTTP 200 HTML application shell; no valid robots directives published.', 'sitemaps': []}
        self.robot = RobotFileParser()
        self.robot.parse(text.splitlines())
        self.delay = max(self.delay, self.robot.crawl_delay(USER_AGENT) or self.robot.crawl_delay('*') or 0)
        return {'status': 'parsed' if meta['status'] == 200 else 'not_found', 'source_url': BASE + 'robots.txt',
                'content': text, 'sitemaps': self.robot.site_maps() or []}
