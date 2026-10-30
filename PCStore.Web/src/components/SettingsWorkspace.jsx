import { useEffect, useState } from 'react';
import { Database, KeyRound, Loader2, Save, Server } from 'lucide-react';
import { adminService, ensureAdminCsrfToken, getApiErrorMessage } from '../services/api';

const inputClass = 'mt-1 w-full rounded-xl border border-slate-200 bg-white px-3 py-2.5 text-sm text-slate-900 outline-none transition focus:border-blue-500 focus:ring-2 focus:ring-blue-100';
const labelClass = 'block text-xs font-semibold text-slate-600';

export default function SettingsWorkspace({
  systemConfiguration,
  updateField,
  status,
  health,
  action,
  onTestDatabase,
  onTestLegacyAi,
  onCancel,
  onSaveSystem,
  legacySettings,
  onChangeLegacy,
  onSaveLegacy,
}) {
  const [localAi, setLocalAi] = useState(null);
  const [gemini, setGemini] = useState(null);
  const [keyState, setKeyState] = useState({ configured: false, unreadable: false });
  const [healthNote, setHealthNote] = useState(null);
  const [notice, setNotice] = useState('');
  const [geminiResult, setGeminiResult] = useState(null);
  const [busy, setBusy] = useState('');

  const applyView = view => {
    setLocalAi({ ...view.ollama });
    setGemini({
      apiKey: '',
      modelName: view.gemini.modelName,
      fallbackText: (view.gemini.fallbackModelNames || []).join(', '),
      baseUrl: view.gemini.baseUrl,
      apiVersion: view.gemini.apiVersion,
      timeoutSeconds: view.gemini.timeoutSeconds,
      connectTimeoutSeconds: view.gemini.connectTimeoutSeconds,
      temperature: view.gemini.temperature,
      maxOutputTokens: view.gemini.maxOutputTokens,
      isEnabled: view.gemini.isEnabled,
    });
    setKeyState({ configured: view.geminiApiKeyConfigured, unreadable: view.geminiApiKeyUnreadable });
    if (view.ollamaHealth) setHealthNote(view.ollamaHealth);
  };

  useEffect(() => {
    let active = true;
    (async () => {
      try {
        await ensureAdminCsrfToken();
        const view = await adminService.getAiProviderSettings();
        if (active) applyView(view);
      } catch (error) {
        if (active) setNotice(getApiErrorMessage(error, 'Không tải được cấu hình AI local.'));
      }
    })();
    return () => { active = false; };
  }, []);

  const saveOllama = async () => {
    setBusy('ollama');
    setNotice('');
    try {
      const view = await adminService.saveOllamaSettings({
        baseUrl: localAi.baseUrl,
        modelName: localAi.modelName,
        keepAlive: localAi.keepAlive,
        numCtx: Number(localAi.numCtx),
        timeoutSeconds: Number(localAi.timeoutSeconds),
        connectTimeoutSeconds: Number(localAi.connectTimeoutSeconds),
        isMaintenanceMode: localAi.isMaintenanceMode,
      });
      applyView({ ...view, ollamaHealth: healthNote });
      setNotice('Đã lưu AI local. Cấu hình có hiệu lực ngay, không cần khởi động lại.');
    } catch (error) {
      setNotice(getApiErrorMessage(error, 'Không lưu được Ollama.'));
    } finally {
      setBusy('');
    }
  };

  const saveGemini = async () => {
    setBusy('gemini');
    setNotice('');
    try {
      const view = await adminService.saveGeminiSettings({
        apiKey: gemini.apiKey,
        modelName: gemini.modelName,
        fallbackModelNames: gemini.fallbackText.split(',').map(item => item.trim()).filter(Boolean),
        baseUrl: gemini.baseUrl,
        apiVersion: gemini.apiVersion,
        timeoutSeconds: Number(gemini.timeoutSeconds),
        connectTimeoutSeconds: Number(gemini.connectTimeoutSeconds),
        temperature: Number(gemini.temperature),
        maxOutputTokens: Number(gemini.maxOutputTokens),
        isEnabled: gemini.isEnabled,
      });
      applyView({ ...view, ollamaHealth: healthNote });
      setNotice('Đã lưu Gemini dự phòng.');
    } catch (error) {
      setNotice(getApiErrorMessage(error, 'Không lưu được Gemini.'));
    } finally {
      setBusy('');
    }
  };

  const test = async provider => {
    setBusy(`test-${provider}`);
    if (provider === 'gemini') setGeminiResult(null);
    try {
      const result = provider === 'ollama' ? await adminService.testOllamaConnection() : await adminService.testGeminiConnection();
      const text = result.message || (result.isHealthy ? 'Kết nối thành công.' : 'Kết nối không thành công.');
      if (provider === 'ollama') {
        setHealthNote({ isHealthy: result.isHealthy, message: text, latencyMilliseconds: result.latencyMilliseconds });
        setNotice(text);
      } else {
        setGeminiResult({ ok: Boolean(result.isHealthy), text: result.latencyMilliseconds != null ? `${text} · ${result.latencyMilliseconds} ms` : text });
      }
    } catch (error) {
      const text = getApiErrorMessage(error, 'Không kiểm tra được kết nối.');
      if (provider === 'gemini') setGeminiResult({ ok: false, text });
      else setNotice(text);
    } finally {
      setBusy('');
    }
  };

  return (
    <div className="space-y-4">
      {notice && <div className="rounded-2xl border border-blue-100 bg-blue-50 px-4 py-3 text-sm text-blue-900">{notice}</div>}

      <section className="rounded-3xl border border-blue-100 bg-white p-5 shadow-sm">
        <header className="mb-4 flex flex-wrap items-start justify-between gap-3">
          <div className="flex items-start gap-3">
            <span className="flex h-10 w-10 items-center justify-center rounded-2xl bg-blue-600 text-white"><Server className="h-5 w-5" /></span>
            <div>
              <p className="text-[11px] font-bold uppercase tracking-wide text-blue-600">Máy này</p>
              <h3 className="text-lg font-black text-slate-900">AI local — Ollama</h3>
              <p className="mt-1 max-w-xl text-sm text-slate-500">Chat và FAQ dùng Ollama trước. Bật bảo trì thì hệ thống chuyển sang Gemini.</p>
            </div>
          </div>
          <span className={`rounded-full px-3 py-1 text-xs font-bold ${localAi?.isMaintenanceMode ? 'bg-amber-100 text-amber-800' : 'bg-emerald-100 text-emerald-800'}`}>
            {localAi?.isMaintenanceMode ? 'Đang bảo trì' : 'Đang dùng local'}
          </span>
        </header>
        {healthNote && (
          <p className={`mb-4 rounded-2xl px-4 py-3 text-sm ${healthNote.isHealthy ? 'bg-emerald-50 text-emerald-800' : 'bg-rose-50 text-rose-800'}`}>
            {healthNote.message}{healthNote.latencyMilliseconds != null ? ` · ${healthNote.latencyMilliseconds} ms` : ''}
          </p>
        )}
        {localAi ? (
          <div className="grid gap-3 md:grid-cols-2">
            <Field label="Địa chỉ Ollama" value={localAi.baseUrl} onChange={value => setLocalAi({ ...localAi, baseUrl: value })} />
            <Field label="Model" value={localAi.modelName} onChange={value => setLocalAi({ ...localAi, modelName: value })} />
            <Field label="Giữ model trong bộ nhớ" value={localAi.keepAlive} onChange={value => setLocalAi({ ...localAi, keepAlive: value })} />
            <Field label="Độ dài ngữ cảnh" type="number" value={localAi.numCtx} onChange={value => setLocalAi({ ...localAi, numCtx: value })} />
            <Field label="Thời gian chờ trả lời (giây)" type="number" value={localAi.timeoutSeconds} onChange={value => setLocalAi({ ...localAi, timeoutSeconds: value })} />
            <Field label="Thời gian chờ kết nối (giây)" type="number" value={localAi.connectTimeoutSeconds} onChange={value => setLocalAi({ ...localAi, connectTimeoutSeconds: value })} />
            <label className="flex items-center gap-3 rounded-2xl border border-slate-200 bg-slate-50 px-4 py-3 text-sm font-semibold text-slate-800 md:col-span-2">
              <input type="checkbox" checked={localAi.isMaintenanceMode} onChange={event => setLocalAi({ ...localAi, isMaintenanceMode: event.target.checked })} />
              Chế độ bảo trì — tạm ngừng Ollama và dùng Gemini
            </label>
            <div className="flex flex-wrap gap-2 md:col-span-2">
              <button type="button" onClick={() => test('ollama')} disabled={busy !== ''} className="rounded-xl border border-slate-300 px-4 py-2 text-sm font-bold text-slate-700">{busy === 'test-ollama' ? 'Đang kiểm tra...' : 'Kiểm tra Ollama'}</button>
              <button type="button" onClick={saveOllama} disabled={busy !== ''} className="rounded-xl bg-blue-600 px-4 py-2 text-sm font-bold text-white">{busy === 'ollama' ? 'Đang lưu...' : 'Lưu AI local'}</button>
            </div>
          </div>
        ) : <Loader2 className="h-5 w-5 animate-spin text-blue-600" />}
      </section>

      <section className="rounded-3xl border border-slate-200 bg-white p-5 shadow-sm">
        <header className="mb-4 flex items-start gap-3">
          <span className="flex h-10 w-10 items-center justify-center rounded-2xl bg-slate-900 text-white"><KeyRound className="h-5 w-5" /></span>
          <div>
            <p className="text-[11px] font-bold uppercase tracking-wide text-slate-500">Khi Ollama không trả lời</p>
            <h3 className="text-lg font-black text-slate-900">Gemini dự phòng</h3>
            <p className="mt-1 text-sm text-slate-500">Key được che. Để trống ô key nếu muốn giữ key đang lưu.</p>
          </div>
        </header>
        {gemini && (
          <div className="grid gap-3 md:grid-cols-2">
            <label className={`${labelClass} md:col-span-2`}>API Key
              <input type="password" autoComplete="new-password" value={gemini.apiKey} placeholder={keyState.configured ? 'Đã lưu — để trống để giữ nguyên' : 'Chưa có key'} onChange={event => setGemini({ ...gemini, apiKey: event.target.value })} className={inputClass} />
            </label>
            {keyState.unreadable && <p className="text-sm font-semibold text-rose-600 md:col-span-2">Key đã lưu không đọc được. Hãy nhập key mới.</p>}
            <Field label="Model chính" value={gemini.modelName} onChange={value => setGemini({ ...gemini, modelName: value })} />
            <Field label="Model dự phòng, cách nhau bởi dấu phẩy" value={gemini.fallbackText} onChange={value => setGemini({ ...gemini, fallbackText: value })} />
            <Field label="Địa chỉ" value={gemini.baseUrl} onChange={value => setGemini({ ...gemini, baseUrl: value })} />
            <Field label="Phiên bản API" value={gemini.apiVersion} onChange={value => setGemini({ ...gemini, apiVersion: value })} />
            <Field label="Timeout (giây)" type="number" value={gemini.timeoutSeconds} onChange={value => setGemini({ ...gemini, timeoutSeconds: value })} />
            <Field label="Timeout kết nối (giây)" type="number" value={gemini.connectTimeoutSeconds} onChange={value => setGemini({ ...gemini, connectTimeoutSeconds: value })} />
            <Field label="Temperature" type="number" value={gemini.temperature} onChange={value => setGemini({ ...gemini, temperature: value })} />
            <Field label="Token tối đa" type="number" value={gemini.maxOutputTokens} onChange={value => setGemini({ ...gemini, maxOutputTokens: value })} />
            <label className="flex items-center gap-3 text-sm font-semibold text-slate-800 md:col-span-2">
              <input type="checkbox" checked={gemini.isEnabled} onChange={event => setGemini({ ...gemini, isEnabled: event.target.checked })} />
              Cho phép dùng Gemini khi Ollama lỗi
            </label>
            <div className="flex flex-wrap gap-2 md:col-span-2">
              <button type="button" onClick={() => test('gemini')} disabled={busy !== ''} className="rounded-xl border border-slate-300 px-4 py-2 text-sm font-bold text-slate-700">{busy === 'test-gemini' ? 'Đang kiểm tra...' : 'Kiểm tra Gemini'}</button>
              <button type="button" onClick={saveGemini} disabled={busy !== ''} className="rounded-xl bg-slate-900 px-4 py-2 text-sm font-bold text-white">{busy === 'gemini' ? 'Đang lưu...' : 'Lưu Gemini'}</button>
            </div>
            {geminiResult && (
              <p role="status" className={`rounded-2xl px-4 py-3 text-sm font-semibold md:col-span-2 ${geminiResult.ok ? 'bg-emerald-50 text-emerald-800' : 'bg-rose-50 text-rose-800'}`}>
                {geminiResult.text}
              </p>
            )}
          </div>
        )}
      </section>

      <section className="rounded-3xl border border-slate-200 bg-white p-5 shadow-sm">
        <header className="mb-4 flex items-start gap-3">
          <span className="flex h-10 w-10 items-center justify-center rounded-2xl bg-slate-100 text-slate-700"><Database className="h-5 w-5" /></span>
          <div>
            <h3 className="text-lg font-black text-slate-900">Cơ sở dữ liệu</h3>
            <p className="mt-1 text-sm text-slate-500">Để trống mật khẩu nếu muốn giữ mật khẩu đã lưu. Đổi database cần khởi động lại API.</p>
          </div>
        </header>
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {[
            ['Host', 'host', 'text'],
            ['Port', 'port', 'number'],
            ['Tên database', 'database', 'text'],
            ['Username', 'username', 'text'],
            ['Mật khẩu', 'password', 'password'],
          ].map(([label, field, type]) => (
            <label key={field} className={labelClass}>{label}
              <input type={type} value={systemConfiguration.database[field]} onChange={event => updateField('database', field, type === 'number' ? Number(event.target.value) : event.target.value)} placeholder={field === 'password' && status?.databasePasswordConfigured ? 'Đã lưu' : ''} className={inputClass} />
            </label>
          ))}
        </div>
        {status?.databaseConnectionChangePendingRestart && <p className="mt-3 rounded-2xl bg-amber-50 px-4 py-3 text-sm text-amber-900">Đổi database đang chờ. Hãy khởi động lại API.</p>}
        {health.database && <p className="mt-3 rounded-2xl bg-slate-50 px-4 py-3 text-sm text-slate-700">{health.database.message}{health.database.latencyMs != null ? ` (${Math.round(health.database.latencyMs)} ms)` : ''}</p>}
        <div className="mt-4 flex flex-wrap justify-end gap-2">
          <button type="button" onClick={onTestDatabase} disabled={Boolean(action)} className="rounded-xl border border-slate-300 px-4 py-2 text-sm font-bold text-slate-700">{action === 'database' ? 'Đang kiểm tra...' : 'Kiểm tra database'}</button>
          <button type="button" onClick={onCancel} disabled={Boolean(action)} className="rounded-xl px-4 py-2 text-sm font-bold text-slate-500">Hủy</button>
          <button type="button" onClick={onSaveSystem} disabled={Boolean(action)} className="inline-flex items-center gap-2 rounded-xl bg-blue-600 px-4 py-2 text-sm font-bold text-white">{action === 'save' ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}Lưu database</button>
        </div>
      </section>

      <details className="rounded-3xl border border-slate-200 bg-white p-5 shadow-sm">
        <summary className="cursor-pointer text-sm font-black text-slate-800">Cấu hình phụ</summary>
        <p className="mt-2 text-sm text-slate-500">Model và API Key đã chuyển lên Gemini dự phòng, hai mục đó để trống tại đây.</p>
        <div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          <label className={labelClass}>Model
            <input value="" readOnly placeholder="Trống" className={inputClass} />
          </label>
          <label className={labelClass}>API Key
            <input type="password" value="" readOnly placeholder="Trống" className={inputClass} />
          </label>
          {[
            ['Temperature', 'temperature', 'number'],
            ['Số token tối đa', 'maxTokens', 'number'],
            ['RAG Top-K', 'ragTopK', 'number'],
            ['RAG Threshold', 'ragThreshold', 'number'],
          ].map(([label, field, type]) => (
            <label key={field} className={labelClass}>{label}
              <input type={type} step={field === 'temperature' || field === 'ragThreshold' ? 0.05 : 1} value={systemConfiguration.ai[field]} onChange={event => updateField('ai', field, type === 'number' ? Number(event.target.value) : event.target.value)} className={inputClass} />
            </label>
          ))}
        </div>
        {health.ai && <p className="mt-3 rounded-2xl bg-slate-50 px-4 py-3 text-sm text-slate-700">{health.ai.message}</p>}
        <button type="button" onClick={onTestLegacyAi} disabled={Boolean(action)} className="mt-4 rounded-xl border border-slate-300 px-4 py-2 text-sm font-bold text-slate-700">{action === 'ai' ? 'Đang kiểm tra...' : 'Kiểm tra API cũ'}</button>
        {legacySettings.length > 0 && (
          <div className="mt-4 space-y-2">
            {legacySettings.map((setting, index) => (
              <label key={setting.key} className="grid items-center gap-2 text-xs font-semibold text-slate-500 sm:grid-cols-[10rem_1fr]">
                <span className="break-all">{setting.key}</span>
                <input value={setting.value} onChange={event => onChangeLegacy(index, event.target.value)} className={inputClass} />
              </label>
            ))}
            <button type="button" onClick={onSaveLegacy} className="rounded-xl bg-slate-900 px-4 py-2 text-sm font-bold text-white">Lưu cấu hình phụ</button>
          </div>
        )}
      </details>
    </div>
  );
}

function Field({ label, value, onChange, type = 'text' }) {
  return (
    <label className={labelClass}>{label}
      <input type={type} value={value ?? ''} onChange={event => onChange(event.target.value)} className={inputClass} />
    </label>
  );
}
