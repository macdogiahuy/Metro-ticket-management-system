"""Discover declared resources and paginate only public, read-only content APIs."""
import json
import hashlib
import re
from datetime import datetime, timedelta, timezone
from pathlib import Path
from urllib.parse import quote, urlencode, urljoin
from .http_client import BASE, PublicClient, now

READ_ENDPOINTS = {
 'GET_MENU_TREE','GET_STATION_TREE','GET_FAQS','GET_FARES_TRIP_RANGE','GET_TICKET_POLICY_TABS',
 'GET_TICKET_POLICY_TAB','GET_PAGE_BY_KEY','GET_LINES_LIST','GET_STATIONS_LIST','GET_ARTICLES',
 'GET_ARTICLE_BY_SLUG','GET_FARES_TIME','GET_FARES_MATRIX','GET_LIST_JOBS','GET_JOB_BY_SLUG',
 'GET_DOCUMENT_GROUPS','GET_DOCUMENT_BY_GROUP','GET_DOCUMENTS_BY_PAGE_TYPE','GET_OFFERS',
 'GET_OFFER_BY_SLUG','GET_GUIDES','GET_GUIDE_BY_SLUG','GET_LINES_MAP','GET_STATION_DETAIL',
 'GET_POIS','GET_STATION_SCHEDULE','GET_BANNERS_BY_URL'
}

def scrub(value):
    """Remove person/contact fields; retain public organizational contact text."""
    excluded = {'author','authorName','createdBy','updatedBy','contactName','fullName','phone','email','reporter','reporterName','phoneNumber','identityNumber','nationalId','applicant','contactPhone','contactEmail'}
    if isinstance(value, dict):
        return {k:scrub(v) for k,v in value.items() if k not in excluded}
    if isinstance(value, list):
        return [scrub(v) for v in value]
    return value

class Crawler:
    def __init__(self, root='output', max_pages=500, depth=4, service_dates=None):
        self.root = Path(root)
        self.client = PublicClient(self.root)
        self.max_pages, self.depth = max_pages, depth
        self.responses = []
        self.endpoints = {}
        self.scripts = {}
        self.started = now()
        self.api_base = None
        self.headers = None
        today=datetime.now(timezone(timedelta(hours=7))).date()
        self.service_dates=service_dates or [(today+timedelta(days=i)).isoformat() for i in range(3)]
        for date in self.service_dates:
            datetime.strptime(date,'%Y-%m-%d')
        self.status='running'

    def bootstrap(self):
        self.robots = self.client.robots()
        _, home = self.client.get(BASE, 'homepage')
        _, sitemap = self.client.get(BASE+'sitemap.xml', 'sitemap')
        self.sitemap_status = 'spa_fallback' if b'<html' in sitemap else 'xml_or_unavailable'
        initial_scripts = re.findall(r'<script[^>]+src="([^"]+)"', home.decode())
        for name in initial_scripts:
            if name.startswith(('runtime.', 'main.')):
                _, body = self.client.get(urljoin(BASE,name), 'explicit_public_script')
                self.scripts[name] = body.decode('utf-8')
        main = next(v for k,v in self.scripts.items() if k.startswith('main.'))
        constants = dict(re.findall(r'e\.([A-Z_]+)="([^"\n]+)"',main))
        block = main.split('e.HANOIMETRO={',1)[1].split('}',1)[0]
        for name, expression in re.findall(r'([A-Z_]+):([^,]+)',block):
            if name not in READ_ENDPOINTS:
                continue
            parts = re.findall(r'e\.([A-Z_]+)|"([^"]*)"',expression)
            self.endpoints[name] = ''.join(constants.get(c,'') if c else literal for c,literal in parts)
        # Fetch only chunk URLs explicitly mapped by the site's runtime. These are never executed.
        runtime = next(v for k,v in self.scripts.items() if k.startswith('runtime.'))
        for chunk, digest in re.findall(r'(\d+):"([a-f0-9]{16})"',runtime):
            name = f'{chunk}.{digest}.js'
            _, body = self.client.get(urljoin(BASE,name), 'explicit_public_route_script')
            self.scripts[name] = body.decode('utf-8')
        _, body = self.client.get(BASE+'assets/config/production.json', 'public_configuration', fresh=True, redact_config=True)
        config = json.loads(body)
        self.api_base = re.sub(r'(?<!:)//+', '/', config['api']['baseUrl']).rstrip('/')
        # The public application publishes this header and uses it for public content.
        # Do not access login, customer, lost-report, applicant, or transaction APIs.
        self.headers = {'X-Api-Key': config['api']['apiKey']} if config['api'].get('apiKey') else {}

    def api(self, name, payload=None, suffix='', pages=None):
        if len(self.responses) >= self.max_pages:
            return None
        if name not in self.endpoints:
            raise ValueError(f'Endpoint not explicitly published: {name}')
        url = self.api_base + self.endpoints[name] + suffix
        meta, body = self.client.get(url, name, method='POST' if payload is not None else 'GET', payload=payload, headers=self.headers)
        try:
            data = json.loads(body)
        except (ValueError, UnicodeDecodeError):
            data = None
        clean = scrub(data)
        # API evidence itself is scrubbed, with original hash kept in request metadata.
        if clean is not None:
            evidence_path=Path(meta['evidence_path'])
            evidence_path.write_text(json.dumps(clean, ensure_ascii=False, indent=2), encoding='utf-8')
            meta['evidence_redacted'] = clean != data
            meta['evidence_sha256']=hashlib.sha256(evidence_path.read_bytes()).hexdigest()
            evidence_path.with_suffix('.json').write_text(json.dumps(meta,ensure_ascii=False,indent=2),encoding='utf-8')
        response = {'request_id': f'request_{len(self.responses)+1:04d}', 'endpoint': name,
                    'source_url': url, 'source_pages': pages or [], 'payload': payload,
                    'metadata': meta, 'response': clean}
        self.responses.append(response)
        self.checkpoint()
        return clean.get('data') if isinstance(clean,dict) and clean.get('messageCode') == '00' else None

    def paginate(self,name,payload,page_url):
        records=[]
        page=0
        while len(self.responses)<self.max_pages:
            data=self.api(name,{**payload,'page':page},pages=[page_url])
            container = data.get('articles',data) if isinstance(data,dict) else data
            items = container.get('items',[]) if isinstance(container,dict) else container if isinstance(container,list) else []
            records.extend(items)
            if not isinstance(container,dict) or page+1 >= container.get('totalPages',1):
                break
            page+=1
        return records

    def checkpoint(self):
        (self.root/'crawl_responses.json').write_text(json.dumps(self.responses,ensure_ascii=False,indent=2),encoding='utf-8')
        (self.root/'crawl_state.json').write_text(json.dumps({'started_at':self.started,'checkpoint_at':now(),'status':self.status,'service_dates':self.service_dates,'robots':getattr(self,'robots',None),'sitemap_status':getattr(self,'sitemap_status',None),'api_base_url':self.api_base,'endpoints':self.endpoints,'scripts':list(self.scripts),'limits':{'max_pages':self.max_pages,'crawl_depth':self.depth},'requests':self.client.events},ensure_ascii=False,indent=2),encoding='utf-8')

    def collect(self):
        self.bootstrap()
        self.api('GET_MENU_TREE',pages=[BASE])
        self.api('GET_STATION_TREE',pages=[BASE,BASE+'line/map'])
        self.api('GET_FAQS',pages=[BASE+'instruct/question'])
        self.api('GET_FARES_TRIP_RANGE',pages=[BASE])
        tabs=self.api('GET_TICKET_POLICY_TABS',pages=[BASE+'fares/ticket-policy'])
        if isinstance(tabs,list):
            for tab in tabs:
                key=tab.get('key') or tab.get('tabKey')
                if key:
                    self.api('GET_TICKET_POLICY_TAB',suffix='?'+urlencode({'tab':key}),pages=[BASE+'fares/ticket-policy?'+urlencode({'tab':key})])
        self.api('GET_FARES_TIME',{'lineId':None,'ticketType':None,'category':'normal','page':0,'pageSize':10},pages=[BASE+'fares/home'])
        for name,payload,url in [
            ('GET_LINES_LIST',{'code':None,'status':None,'keyword':None,'pageSize':10},'line/map'),
            ('GET_ARTICLES',{'categoryId':None,'pageSize':12},'news'),
            ('GET_OFFERS',{'pageSize':9},'offer'),
            ('GET_GUIDES',{'pageSize':12},'instruct/home'),
            ('GET_LIST_JOBS',{'pageSize':12},'contact/recruitment')]:
            self.paginate(name,payload,BASE+url)
        self.collect_details()
        self.status='completed_public_module_scope' if len(self.responses)<self.max_pages else 'page_limit_reached'
        self.checkpoint()

    def data_for(self,name):
        return [r['response']['data'] for r in self.responses if r['endpoint']==name and isinstance(r['response'],dict) and r['response'].get('messageCode')=='00']

    def collect_details(self):
        all_scripts=' '.join(self.scripts.values())
        keys=sorted(set(re.findall(r'this\.[A-Z_]*KEY="([^"]+)"',all_scripts)))
        key_pages={'about-info-cards':'about/home','benefits':'instruct/benefits','contact':'contact/home','culture':'instruct/culture','first-ride-guide':'instruct/home','homepage-app':'','poi-categories':'station-amenities','regulations':'instruct/regulations'}
        for key in keys:
            if key in key_pages:  # organizational chart has personal names; omitted deliberately.
                self.api('GET_PAGE_BY_KEY',suffix='/'+quote(key),pages=[BASE+key_pages[key]])
        for page_type,page_url in [(1,'about/enterprise-information'),(2,'about/company-regulation')]:
            groups=self.paginate('GET_DOCUMENT_GROUPS',{'pageType':page_type,'pageSize':10},BASE+page_url)
            for group in groups:
                self.api('GET_DOCUMENT_BY_GROUP',{'docType':group.get('docType'),'year':[group.get('year')]},pages=[BASE+page_url])
        for category in self.data_for('GET_ARTICLES')[0].get('categories',[]):
            if category['id'] != self.data_for('GET_ARTICLES')[0].get('activeCategoryId'):
                self.paginate('GET_ARTICLES',{'categoryId':category['id'],'pageSize':12},BASE+'news')
        for collection,endpoint,prefix in [('GET_ARTICLES','GET_ARTICLE_BY_SLUG','news/'),('GET_GUIDES','GET_GUIDE_BY_SLUG','instruct/'),('GET_OFFERS','GET_OFFER_BY_SLUG','offer/'),('GET_LIST_JOBS','GET_JOB_BY_SLUG','contact/recruitment-detail?slug=')]:
            records={}
            for data in self.data_for(collection):
                bucket=data.get('articles',data)
                for item in bucket.get('items',[]):
                    records[item['id']]=item
            for item in records.values():
                if item.get('slug'):
                    self.api(endpoint,suffix='/'+quote(item['slug']),pages=[BASE+prefix+quote(item['slug'])])
        self.api('GET_POIS',suffix='?'+urlencode({'page':1,'pageSize':12}),pages=[BASE+'station-amenities'])
        pois=self.data_for('GET_POIS')
        if pois and isinstance(pois[0],dict):
            page=2
            total=pois[0].get('totalPages',1)
            while page<=total and len(self.responses)<self.max_pages:
                self.api('GET_POIS',suffix='?'+urlencode({'page':page,'pageSize':12}),pages=[BASE+'station-amenities'])
                page+=1
        lines=self.data_for('GET_STATION_TREE')[0]
        self.api('GET_FARES_TIME',{'lineId':None,'ticketType':None,'category':None,'page':0,'pageSize':10},pages=[BASE+'fares/home'])
        for line in lines:
            self.paginate('GET_STATIONS_LIST',{'lineId':line['id'],'type':None,'status':None,'keyword':None,'pageSize':20},BASE+'line/map')
            for station in line['stations']:
                page=BASE+'line/map?station='+station['slug']
                self.api('GET_STATION_DETAIL',suffix='/'+quote(station['slug']),pages=[page])
                for extra in ['/exits','/bus-routes']:
                    self.api('GET_STATION_DETAIL',suffix='/'+quote(station['slug'])+extra,pages=[page])
                for iso_date in self.service_dates:
                    date=datetime.strptime(iso_date,'%Y-%m-%d').strftime('%d-%m-%Y')
                    for direction in ['inbound','outbound']:
                        self.api('GET_STATION_SCHEDULE',{'stationId':station['id'],'direction':direction,'targetDate':date+' 00:00:00','period':None},pages=[page])
            # Read each direction explicitly instead of assuming matrix symmetry.
            for origin in line['stations']:
                for destination in line['stations']:
                    if origin['id']!=destination['id']:
                        self.api('GET_FARES_MATRIX',{'lineId':line['id'],'fromStationId':origin['id'],'toStationId':destination['id']},pages=[BASE+'fares/ticket-policy?tab=luot'])

def run(root='output',max_pages=500,depth=4,service_dates=None):
    crawler=Crawler(root,max_pages,depth,service_dates)
    try:
        crawler.collect()
    except Exception:
        crawler.status='stopped_access_or_execution_error'
        crawler.checkpoint()
        raise
