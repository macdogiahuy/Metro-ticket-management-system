"""Generate reviewable seed SQL for the independent reference schema."""
import json
from pathlib import Path

def seed_sql(dataset,schema):
    tables={t['name']:t for t in schema['database']['tables'] if t['name'] in dataset}
    ordered=[]
    pending=set(tables)
    while pending:
        ready=sorted(t for t in pending if all(c['references'] is None or c['references']['table']==t or c['references']['table'] not in pending for c in tables[t]['columns']))
        if not ready:
            raise ValueError('Circular dependency between reference tables')
        ordered.extend(ready)
        pending.difference_update(ready)
    lines=['-- Public reference snapshot, including flagged source anomalies. Not verified production data.',
           '-- Run schema.sql first, then this file against an empty research database.',
           'BEGIN;', 'SET LOCAL search_path = metro_reference, public;', 'SET LOCAL standard_conforming_strings = on;']
    for table in ordered:
        columns=', '.join('"'+c['name']+'"' for c in tables[table]['columns'])
        value=json.dumps(dataset[table],ensure_ascii=False,separators=(',',':')).replace("'","''")
        lines.append(f'INSERT INTO "{table}" ({columns}) SELECT {columns} FROM json_populate_recordset(NULL::"{table}", \'{value}\'::json) ON CONFLICT ("id") DO NOTHING;')
    lines.append('COMMIT;')
    return '\n\n'.join(lines)+'\n'

def write_seed(root='output'):
    root=Path(root)
    dataset=json.loads((root/'normalized_data.json').read_text(encoding='utf-8'))['data']
    schema=json.loads((root/'database_schema.json').read_text(encoding='utf-8'))
    path=root/'seed_reference.sql'
    path.write_text(seed_sql(dataset,schema),encoding='utf-8')
    return str(path)
