import { useCallback, useEffect, useState } from 'react';
import apiClient from '../../lib/axios';

type ServiceProbe = {
  status: string;
  latencyMs: number;
  detail: string;
};

type HealthResponse = {
  status: string;
  services: Record<string, ServiceProbe>;
  checkedAt: string;
};

const serviceLabels: Record<string, string> = {
  postgres: 'Lakebase Postgres',
  objectStorage: 'Object storage',
};

export function NeonStatus() {
  const [health, setHealth] = useState<HealthResponse | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);

    try {
      const { data } = await apiClient.get<HealthResponse>('/health', { timeout: 15000 });
      setHealth(data);
    } catch {
      setHealth(null);
      setError('Không gọi được API. Chạy backend tại http://localhost:8080.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const healthy = health?.status === 'healthy';

  return (
    <aside
      aria-label="Trạng thái dịch vụ Neon"
      aria-live="polite"
      style={{
        position: 'fixed',
        right: 16,
        bottom: 16,
        zIndex: 50,
        width: 320,
        padding: 16,
        borderRadius: 12,
        border: '1px solid rgba(127, 127, 127, 0.25)',
        background: 'rgba(20, 20, 24, 0.92)',
        color: '#f5f5f5',
        font: '13px/1.45 system-ui, "Segoe UI", Roboto, sans-serif',
        boxShadow: '0 10px 30px rgba(0, 0, 0, 0.35)',
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 8 }}>
        <strong style={{ fontSize: 14 }}>Neon services</strong>
        <span
          style={{
            padding: '2px 8px',
            borderRadius: 999,
            fontSize: 11,
            fontWeight: 600,
            background: loading ? 'rgba(148, 163, 184, 0.25)' : healthy ? '#166534' : '#7f1d1d',
          }}
        >
          {loading ? 'đang kiểm tra' : healthy ? 'healthy' : 'unhealthy'}
        </span>
      </div>

      {error ? (
        <p style={{ margin: '10px 0 0', color: '#fca5a5' }}>{error}</p>
      ) : (
        <ul style={{ listStyle: 'none', margin: '10px 0 0', padding: 0 }}>
          {Object.entries(health?.services ?? {}).map(([name, probe]) => (
            <li
              key={name}
              style={{ display: 'flex', flexDirection: 'column', gap: 2, padding: '6px 0' }}
            >
              <span style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
                <span
                  aria-hidden="true"
                  style={{
                    width: 8,
                    height: 8,
                    borderRadius: '50%',
                    background: probe.status === 'healthy' ? '#22c55e' : '#ef4444',
                  }}
                />
                {serviceLabels[name] ?? name}
              </span>
              <span style={{ color: '#a1a1aa', paddingLeft: 16 }}>
                {probe.latencyMs} ms · {probe.detail}
              </span>
            </li>
          ))}
        </ul>
      )}

      <button
        type="button"
        onClick={() => void load()}
        disabled={loading}
        style={{
          marginTop: 10,
          width: '100%',
          padding: '6px 10px',
          borderRadius: 8,
          border: '1px solid rgba(127, 127, 127, 0.35)',
          background: 'transparent',
          color: 'inherit',
          cursor: loading ? 'progress' : 'pointer',
        }}
      >
        Kiểm tra lại
      </button>
    </aside>
  );
}

export default NeonStatus;
