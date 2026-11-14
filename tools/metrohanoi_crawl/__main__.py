import argparse
import json
import re
from pathlib import Path
from urllib.parse import urljoin
from .http_client import BASE, PublicClient

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('command', choices=['discover', 'fetch', 'crawl', 'export', 'validate'])
    parser.add_argument('--url')
    parser.add_argument('--output', default='output')
    parser.add_argument('--max-pages', type=int, default=500)
    parser.add_argument('--service-dates', nargs='+', help='ISO dates YYYY-MM-DD; default today plus two days in Asia/Saigon')
    args = parser.parse_args()
    if args.command == 'export':
        from .export import export
        print(json.dumps(export(args.output),ensure_ascii=False,indent=2))
        return
    if args.command == 'validate':
        from .validate import validate_output
        report=validate_output(args.output)
        print(json.dumps({k:v for k,v in report.items() if k!='json_files'},ensure_ascii=False,indent=2))
        if report['status']!='passed':
            raise SystemExit(1)
        return
    if args.command == 'crawl':
        from .crawler import run
        run(args.output,args.max_pages,service_dates=args.service_dates)
        return
    client = PublicClient(Path(args.output))
    robots = client.robots()
    if args.command == 'discover':
        _, home = client.get(BASE, 'homepage')
        _, sitemap = client.get(BASE + 'sitemap.xml', 'sitemap')
        scripts = re.findall(r'<script[^>]+src="([^"]+)"', home.decode())
        for script in scripts:
            if script.startswith(('main.', 'runtime.')):
                client.get(urljoin(BASE, script), 'explicit_public_script')
        (Path(args.output) / 'discovery.json').write_text(json.dumps({'robots': robots, 'scripts': scripts, 'requests': client.events}, ensure_ascii=False, indent=2), encoding='utf-8')
    elif args.command == 'fetch':
        client.get(args.url, 'explicit_public_resource')

if __name__ == '__main__':
    main()
