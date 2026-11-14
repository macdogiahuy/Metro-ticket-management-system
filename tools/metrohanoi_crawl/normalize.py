"""Traceable relational reference records, with deterministic independent IDs."""
import html
import json
import re
import unicodedata
import uuid
from collections import defaultdict
from urllib.parse import unquote

NAMESPACE = uuid.UUID('fc57132a-7e8b-51ea-a99f-46adb3ca5104')

def identifier(table, key):
    return str(uuid.uuid5(NAMESPACE, table+':'+str(key)))

def snake(key):
    return re.sub(r'(?<!^)(?=[A-Z])','_',key).lower()

def clean_text(value):
    return unicodedata.normalize('NFC',re.sub(r'\s+',' ',html.unescape(value))).strip()

def plain(value):
    return clean_text(re.sub(r'<[^>]*>',' ',value or ''))

def normalized(value):
    if isinstance(value,dict):
        return {snake(k):normalized(v) for k,v in value.items()}
    if isinstance(value,list):
        return [normalized(v) for v in value]
    if isinstance(value,str):
        # URLs and HTML retain bytes/format; display text is NFC normalized.
        return value if value.startswith(('http://','https://')) or re.search(r'<[a-zA-Z]',value) else clean_text(value) or None
    return value

class Normalizer:
    def __init__(self,responses):
        self.responses=responses
        self.tables=defaultdict(dict)
        self.duplicates=defaultdict(int)
        self.conflicts=[]

    def put(self,table,key,values,r,path,kind='observed'):
        rid=identifier(table,key)
        ref={'request_id':r['request_id'],'source_url':r['source_url'],'json_pointer':path,
             'source_pages':r['source_pages'],'retrieved_at':r['metadata']['retrieved_at']}
        row={'id':rid,'source_id':str(key),'evidence_type':kind,**normalized(values)}
        if rid in self.tables[table]:
            old=self.tables[table][rid]
            self.duplicates[table]+=1
            for field,value in row.items():
                if field in ('id','source_id','evidence_type'):
                    continue
                if value is not None and old.get(field) not in (None,value):
                    # Public labels and live presentation fields can change between calls.
                    self.conflicts.append({'table':table,'record_id':rid,'field':field,'values':[old[field],value], 'source_refs':[old['source_refs'][0],ref]})
                if value is not None:
                    old[field]=value
            if ref not in old['source_refs']:
                old['source_refs'].append(ref)
        else:
            row['source_refs']=[ref]
            self.tables[table][rid]=row
        return rid

    def content(self,table,item,r,path):
        values={k:v for k,v in item.items() if k not in ['id','categoryId','tags','relatedPosts','relatedArticles']}
        category=item.get('categoryId')
        values['category_id']=identifier('content_categories',category) if category else None
        if category and identifier('content_categories',category) not in self.tables['content_categories']:
            self.put('content_categories',category,{'name':None,'slug':None,'section':table,'is_identifier_only':True},r,path+'/categoryId','observed_identifier_only')
        if item.get('content'):
            values['content_text']=plain(item['content'])
        rid=self.put(table,item['id'],values,r,path)
        for index,tag in enumerate(item.get('tags') or []):
            label=tag.get('name') if isinstance(tag,dict) else tag
            key=tag.get('id',label) if isinstance(tag,dict) else label
            tid=self.put('tags',key,{'name':label},r,path+f'/tags/{index}')
            self.put('article_tags',f'{item["id"]}:{key}',{'article_id':rid,'tag_id':tid},r,path+f'/tags/{index}')

    def run(self):
        for r in self.responses:
            response=r.get('response')
            if not isinstance(response,dict) or response.get('messageCode')!='00':
                continue
            d=response.get('data')
            endpoint=r['endpoint']
            if endpoint=='GET_MENU_TREE':
                def walk(items,parent=None,pointer='/data'):
                    for i,item in enumerate(items or []):
                        path=pointer+f'/{i}'
                        rid=self.put('menu_items',item['id'],{**{k:v for k,v in item.items() if k not in ['id','children']},'parent_id':parent},r,path)
                        walk(item.get('children'),rid,path+'/children')
                walk(d)
            elif endpoint=='GET_STATION_TREE':
                for i,line in enumerate(d):
                    lp=f'/data/{i}'
                    lid=self.put('lines',line['id'],{k:v for k,v in line.items() if k not in ['id','stations','timeSlots']},r,lp)
                    for j,slot in enumerate(line.get('timeSlots') or []):
                        self.put('line_time_slots',f'{line["id"]}:{j}',{'line_id':lid,**slot},r,lp+f'/timeSlots/{j}')
                    for j,station in enumerate(line['stations']):
                        sp=lp+f'/stations/{j}'
                        sid=self.put('stations',station['id'],{k:v for k,v in station.items() if k not in ['id','lineId','images'] and not k.startswith(('has','exit'))},r,sp)
                        self.put('line_stations',line['id']+':'+station['id'],{'line_id':lid,'station_id':sid,'sequence':station['sortOrder'],'station_code':str(station['stationCode'])},r,sp)
                        for code,label,field in [('parking','Bãi đỗ xe','hasParking'),('elevator','Thang máy','hasElevator'),('toilet','WC','hasToilet'),('wifi','Wifi','hasWifi')]:
                            aid=self.put('amenity_types',code,{'code':code,'name':label},r,sp+'/'+field,'observed_reference')
                            self.put('station_amenities',station['id']+':'+code,{'station_id':sid,'amenity_type_id':aid,'is_available':station.get(field)},r,sp+'/'+field)
                        for k,img in enumerate(station.get('images') or []):
                            self.put('station_images',img.get('id') or station['id']+f':{k}',{'station_id':sid,'url':img['fileUrl'],'alt_text':img.get('altText'),'sort_order':img.get('sortOrder')},r,sp+f'/images/{k}')
            elif endpoint=='GET_LINES_LIST':
                for i,line in enumerate(d.get('items',[])):
                    self.put('lines',line['id'],{k:v for k,v in line.items() if k!='id'},r,f'/data/items/{i}')
            elif endpoint=='GET_STATION_DETAIL':
                slug=r['source_url'].split('/stations/',1)[1].split('/')[0]
                station=next((s for s in self.tables['stations'].values() if s.get('slug')==slug),None)
                if station is None:
                    continue
                sid=station['id']
                if r['source_url'].endswith('/exits'):
                    for i,exit in enumerate(d or []):
                        self.put('station_exits',station['source_id']+':'+exit['exitCode'],{'station_id':sid,'exit_code':exit['exitCode'],'name':exit['name']},r,f'/data/{i}')
                elif r['source_url'].endswith('/bus-routes'):
                    for i,bus in enumerate(d or []):
                        bid=self.put('bus_routes',bus['routeNo'],{'code':bus['routeNo'],'name':bus['routeName']},r,f'/data/{i}')
                        self.put('station_bus_routes',station['source_id']+':'+bus['routeNo'],{'station_id':sid,'bus_route_id':bid,'direction_note':bus.get('directionNote')},r,f'/data/{i}')
                elif isinstance(d,dict):
                    self.put('stations',d['id'],{'frequency_min':d.get('frequencyMin'),'frequency_max':d.get('frequencyMax')},r,'/data')
                    for i,train in enumerate(d.get('nextTrains') or []):
                        self.put('next_train_observations',r['request_id']+f':{i}',{'station_id':sid,**train},r,f'/data/nextTrains/{i}')
            elif endpoint=='GET_STATION_SCHEDULE' and isinstance(d,dict):
                date=d['targetDate']
                boardkey=f'{d["stationId"]}:{d["direction"]}:{date}'
                board=self.put('schedule_boards',boardkey,{'station_id':identifier('stations',d['stationId']),'direction':d['direction'],'service_date':date,'day_type':d.get('dayType')},r,'/data')
                for i,row in enumerate(d.get('schedules') or []):
                    values={k:v for k,v in row.items() if k not in ['id','lineId','stationId','originStationId','destStationId','lineName','lineCode','stationName','originStationName','destStationName']}
                    values.update({'board_id':board,'line_id':identifier('lines',row['lineId']),'station_id':identifier('stations',row['stationId']), 'origin_station_id':identifier('stations',row['originStationId']) if row.get('originStationId') else None,'destination_station_id':identifier('stations',row['destStationId']) if row.get('destStationId') else None,'service_date':date,'source_schedule_id':row['id']})
                    self.put('scheduled_departures',row['id']+':'+date,values,r,f'/data/schedules/{i}')
            elif endpoint=='GET_FARES_TRIP_RANGE':
                for i,item in enumerate(d['items']):
                    self.put('fare_products',item['ticketType'],{'code':item['ticketType'],'summary_title':item['title'],'summary_description':item.get('description'),'summary_min_price':item.get('minPrice'),'summary_max_price':item.get('maxPrice'),'currency':'VND'},r,f'/data/items/{i}','observed_reference')
            elif endpoint=='GET_FARES_TIME':
                for i,line in enumerate(d or []):
                    for j,fare in enumerate(line.get('timeFares') or []):
                        fp=f'/data/{i}/timeFares/{j}'
                        pid=self.put('fare_products',fare['ticketType'],{'code':fare['ticketType'],'name':fare['ticketName'],'advertised_duration':fare.get('ticketNumber'),'description':fare.get('conditions'),'currency':'VND'},r,fp,'observed_reference')
                        self.put('time_fares',fare['id'],{'line_id':identifier('lines',line['id']),'fare_product_id':pid,'category':fare.get('category'),'category_name':fare.get('categoryName'),'price':fare['price'],'currency':'VND','conditions':fare.get('conditions'),'source_updated_at':fare.get('updatedAt'),'effective_from':None,'effective_to':None},r,fp)
            elif endpoint=='GET_FARES_MATRIX':
                for i,fare in enumerate(d or []):
                    fp=f'/data/{i}'
                    fid=self.put('fare_pairs',fare['id'],{'line_id':identifier('lines',fare['lineId']),'from_station_id':identifier('stations',fare['fromStationId']),'to_station_id':identifier('stations',fare['toStationId']),'price_adult':fare['priceAdult'],'price_priority_raw':fare.get('pricePriority'),'payment_method':None,'currency':'VND','total_stations':fare.get('totalStations'),'notes':fare.get('notes'),'source_updated_at':fare.get('updatedAt'),'effective_from':None,'effective_to':None},r,fp)
                    for j,stop in enumerate(fare.get('stations') or []):
                        self.put('fare_path_stations',fare['id']+f':{r["request_id"]}:{j}',{'fare_pair_id':fid,'station_id':identifier('stations',stop['id']),'request_id':r['request_id'],'published_sequence':j+1},r,fp+f'/stations/{j}')
            elif endpoint=='GET_FAQS':
                for i,topic in enumerate(d or []):
                    tp=f'/data/{i}'
                    tid=self.put('faq_topics',topic['id'],{k:v for k,v in topic.items() if k not in ['id','items']},r,tp)
                    for j,faq in enumerate(topic.get('items') or []):
                        self.put('faqs',faq['id'],{**{k:v for k,v in faq.items() if k not in ['id','topicId']},'topic_id':tid,'answer_text':plain(faq.get('answer'))},r,tp+f'/items/{j}')
            elif endpoint=='GET_ARTICLES':
                for i,category in enumerate(d.get('categories',[])):
                    self.put('content_categories',category['id'],{**{k:v for k,v in category.items() if k!='id'},'is_identifier_only':False},r,f'/data/categories/{i}')
                for i,item in enumerate(d.get('articles',{}).get('items',[])):
                    self.content('articles',item,r,f'/data/articles/items/{i}')
            elif endpoint in ['GET_GUIDES','GET_LIST_JOBS','GET_OFFERS']:
                table={'GET_GUIDES':'guides','GET_LIST_JOBS':'jobs','GET_OFFERS':'offers'}[endpoint]
                for i,item in enumerate(d.get('items',[])):
                    if table=='guides':
                        self.content(table,item,r,f'/data/items/{i}')
                    else:
                        self.put(table,item['id'],{k:v for k,v in item.items() if k!='id'},r,f'/data/items/{i}')
            elif endpoint in ['GET_ARTICLE_BY_SLUG','GET_GUIDE_BY_SLUG','GET_JOB_BY_SLUG','GET_OFFER_BY_SLUG']:
                table={'GET_ARTICLE_BY_SLUG':'articles','GET_GUIDE_BY_SLUG':'guides','GET_JOB_BY_SLUG':'jobs','GET_OFFER_BY_SLUG':'offers'}[endpoint]
                item=d.get('article',d) if isinstance(d,dict) else None
                if item and item.get('id'):
                    if table in ['articles','guides']:
                        self.content(table,item,r,'/data/article' if 'article' in d else '/data')
                    else:
                        self.put(table,item['id'],{**{k:v for k,v in item.items() if k!='id'},'description_text':plain(item.get('description'))},r,'/data')
            elif endpoint=='GET_TICKET_POLICY_TAB':
                self.put('ticket_policy_tabs',d['id'],{**{k:v for k,v in d.items() if k not in ['id','guideSteps']},'basic_info_text':plain(d.get('basicInfo')),'usage_instructions_text':plain(d.get('usageInstructions'))},r,'/data')
                for i,step in enumerate(d.get('guideSteps') or []):
                    self.put('policy_guide_steps',step.get('id') or d['id']+f':{i}',{'policy_tab_id':identifier('ticket_policy_tabs',d['id']),**{k:v for k,v in step.items() if k!='id'}},r,f'/data/guideSteps/{i}')
            elif endpoint=='GET_DOCUMENT_GROUPS':
                for i,group in enumerate(d.get('items',[])):
                    key=f'{r["payload"]["pageType"]}:{group["docType"]}:{group["year"]}:{group["title"]}'
                    self.put('document_groups',key,{'page_type':r['payload']['pageType'],**group},r,f'/data/items/{i}')
            elif endpoint=='GET_DOCUMENT_BY_GROUP':
                for i,document in enumerate(d or []):
                    matches=[g for g in self.tables['document_groups'].values() if g['doc_type']==document.get('docType') and g['year']==document.get('year') and g['title']==clean_text(document.get('title',''))]
                    gid=matches[0]['id'] if len(matches)==1 else None
                    did=self.put('documents',document['id'],{**{k:v for k,v in document.items() if k not in ['id','fileUrl','fileList']},'group_id':gid},r,f'/data/{i}')
                    urls=document.get('fileList') or (document.get('fileUrl') or '').split(';')
                    for j,url in enumerate(urls):
                        if url:
                            self.put('document_files',document['id']+':'+url,{'document_id':did,'url':url,'filename':unquote(url.rsplit('/',1)[-1]),'extension':url.rsplit('.',1)[-1].lower(),'sort_order':j+1,'download_status':'url_catalogued_not_downloaded'},r,f'/data/{i}/fileList/{j}' if document.get('fileList') else f'/data/{i}/fileUrl')
            elif endpoint=='GET_PAGE_BY_KEY' and isinstance(d,dict):
                content=d.get('content')
                try:
                    structured=json.loads(content) if isinstance(content,str) else content
                except (ValueError,TypeError):
                    structured=None
                self.put('content_pages',d['key'],{'key':d['key'],'content_original':content,'structured_content':structured,'source_updated_at':d.get('updatedAt')},r,'/data')
            elif endpoint=='GET_POIS':
                for i,poi in enumerate(d.get('items',[])):
                    pp=f'/data/items/{i}'
                    category=poi.get('category') or {}
                    cid=self.put('poi_categories',category['code'],category,r,pp+'/category','observed_reference') if category.get('code') else None
                    pid=self.put('pois',poi['id'],{**{k:v for k,v in poi.items() if k not in ['id','category','stations']},'category_id':cid},r,pp)
                    for j,ref in enumerate(poi.get('stations') or []):
                        station=next((s for s in self.tables['stations'].values() if s.get('slug')==ref.get('slug')),None)
                        # Station tree is processed earlier; preserve a null if an unknown slug appears.
                        self.put('station_pois',poi['id']+':'+ref['slug'],{'poi_id':pid,'station_id':station['id'] if station else None,'source_station_slug':ref['slug']},r,pp+f'/stations/{j}')
        return {table:sorted(rows.values(),key=lambda row:row['id']) for table,rows in self.tables.items()}
