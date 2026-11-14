"""Load and verify the proposal in an explicitly selected empty research database."""
import argparse
import hashlib
import json
import os
import subprocess
from datetime import datetime, timezone
from pathlib import Path


ARTIFACTS = ('schema.sql', 'seed_reference.sql', 'normalized_data.json', 'database_schema.json')


def artifact_hashes(root):
    return {name: hashlib.sha256((Path(root) / name).read_bytes()).hexdigest() for name in ARTIFACTS}


def verified_evidence(root, dataset, schema):
    """Only reuse SQL evidence when all tested input bytes and counts still match."""
    path = Path(root) / 'postgresql_verification.json'
    if not path.exists():
        return None
    evidence = json.loads(path.read_text(encoding='utf-8'))
    expected = {t['name']: len(dataset.get(t['name'], [])) for t in schema['database']['tables']}
    if (evidence.get('status') == 'passed'
            and evidence.get('artifact_sha256') == artifact_hashes(root)
            and evidence.get('actual_record_counts') == expected):
        return evidence
    return None


def verify(root, psql, host, port, user, database):
    root = Path(root).resolve()
    dataset = json.loads((root / 'normalized_data.json').read_text(encoding='utf-8'))['data']
    schema = json.loads((root / 'database_schema.json').read_text(encoding='utf-8'))
    base = [psql, '-X', '-w', '-h', host, '-p', str(port), '-U', user, '-d', database,
            '-v', 'ON_ERROR_STOP=1']
    environment = {**os.environ, 'PGCLIENTENCODING': 'UTF8'}

    def run(*args):
        result = subprocess.run(base + list(args), capture_output=True, encoding='utf-8', errors='replace', env=environment)
        if result.returncode:
            # Avoid echoing a potentially large seed statement in PostgreSQL diagnostics.
            detail = '\n'.join(line[:300] for line in result.stderr.splitlines()[:4])
            raise RuntimeError(f'psql exit {result.returncode}: {detail}')
        return result.stdout.strip()

    if run('-At', '-c', "SELECT count(*) FROM pg_namespace WHERE nspname='metro_reference'") != '0':
        raise ValueError('Use an empty research database: metro_reference already exists. No SQL was loaded.')
    tested_hashes = artifact_hashes(root)
    run('-f', str(root / 'schema.sql'))
    run('-f', str(root / 'seed_reference.sql'))
    counts_sql = ' UNION ALL '.join(
        f"SELECT '{t['name']}' AS name, count(*) AS n FROM metro_reference.\"{t['name']}\""
        for t in schema['database']['tables'])
    query = """SELECT json_build_object(
        'engine_version', version(),
        'actual_record_counts', (SELECT json_object_agg(name,n) FROM (%s) counted),
        'table_names', (SELECT json_agg(tablename ORDER BY tablename) FROM pg_tables WHERE schemaname='metro_reference'),
        'constraints', (SELECT json_agg(json_build_object('name',c.conname,'type',c.contype,'validated',c.convalidated) ORDER BY c.conname)
          FROM pg_constraint c JOIN pg_namespace n ON n.oid=c.connamespace WHERE n.nspname='metro_reference'),
        'index_names', (SELECT json_agg(indexname ORDER BY indexname) FROM pg_indexes WHERE schemaname='metro_reference')
    )""" % counts_sql
    actual = json.loads(run('-At', '-c', query))
    tables = schema['database']['tables']
    expected_counts = {t['name']: len(dataset.get(t['name'], [])) for t in tables}
    errors = []
    if actual['table_names'] != sorted(expected_counts):
        errors.append('PostgreSQL tables differ from the schema catalog.')
    if actual['actual_record_counts'] != expected_counts:
        errors.append('Loaded record counts differ from the normalized snapshot.')
    constraints = actual['constraints']
    expected_fk = {f"fk_{t['name']}_{c['name']}" for t in tables for c in t['columns'] if c.get('references')}
    actual_fk = {c['name'] for c in constraints if c['type'] == 'f'}
    expected_checks = {c['name'] for c in schema['database']['constraints'] if c['type'] == 'check'}
    actual_checks = {c['name'] for c in constraints if c['type'] == 'c'}
    if expected_fk != actual_fk or expected_checks != actual_checks:
        errors.append('Foreign-key or CHECK constraints differ from the schema catalog.')
    if any(not c['validated'] for c in constraints):
        errors.append('A PostgreSQL constraint is not validated.')
    if sum(c['type'] == 'p' for c in constraints) != len(tables):
        errors.append('Primary-key count differs from the schema catalog.')
    if sum(c['type'] == 'u' for c in constraints) != sum(len(t['unique_constraints']) for t in tables):
        errors.append('UNIQUE constraint count differs from the schema catalog.')
    if not {i['name'] for i in schema['database']['indexes']}.issubset(actual['index_names']):
        errors.append('A recommended index is missing.')
    if artifact_hashes(root) != tested_hashes:
        errors.append('Artifacts changed during SQL verification.')
    report = {
        'status': 'failed' if errors else 'passed',
        'verified_at': datetime.now(timezone.utc).isoformat(),
        'scope': 'schema.sql and seed_reference.sql loaded with ON_ERROR_STOP into an empty research database; all table counts and declared constraints checked.',
        'schema_load_exit_code': 0, 'seed_load_exit_code': 0,
        'artifact_sha256': tested_hashes,
        'expected_record_counts': expected_counts,
        'errors': errors,
        **actual,
    }
    (root / 'postgresql_verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', default='output')
    parser.add_argument('--psql', default='psql')
    parser.add_argument('--host', default='127.0.0.1')
    parser.add_argument('--port', type=int, default=5432)
    parser.add_argument('--user', required=True)
    parser.add_argument('--database', required=True)
    args = parser.parse_args()
    report = verify(args.output, args.psql, args.host, args.port, args.user, args.database)
    print(json.dumps({'status': report['status'], 'tables': len(report['actual_record_counts']),
                      'records': sum(report['actual_record_counts'].values()), 'errors': report['errors']}, indent=2))
    if report['status'] != 'passed':
        raise SystemExit(1)


if __name__ == '__main__':
    main()
