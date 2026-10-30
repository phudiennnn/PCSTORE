import { useEffect, useState } from 'react';
import { Download, Loader2, Plus, Upload } from 'lucide-react';
import { adminService, ensureAdminCsrfToken, getApiErrorMessage } from '../services/api';

const emptyForm = () => ({ category: '', question: '', answer: '', keywords: '', priority: 0, isActive: true });
const fieldClass = 'mt-1 w-full rounded-xl border border-slate-300 bg-white px-3 py-2 text-sm text-slate-800 outline-none focus:border-blue-400';

export default function FaqAdminSection({ refreshKey = 0 }) {
  const [pageData, setPageData] = useState(null);
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState('');
  const [needsFill, setNeedsFill] = useState(false);
  const [page, setPage] = useState(1);
  const [form, setForm] = useState(null);
  const [editingId, setEditingId] = useState(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState('');

  const load = async (nextPage = page) => {
    setLoading(true);
    try {
      await ensureAdminCsrfToken();
      const data = await adminService.getFaqs({
        search: search.trim() || undefined,
        category: category || undefined,
        needsFill: needsFill || undefined,
        page: nextPage,
        pageSize: 10,
      });
      setPageData(data);
      setPage(data.page);
    } catch (error) {
      setNotice(getApiErrorMessage(error, 'Không tải được danh sách FAQ.'));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => { load(1); }, [refreshKey]);

  const save = async event => {
    event.preventDefault();
    setSaving(true);
    setNotice('');
    const payload = {
      category: form.category, question: form.question, answer: form.answer, keywords: form.keywords,
      priority: Number(form.priority) || 0, isActive: form.isActive,
    };
    try {
      if (editingId) await adminService.updateFaq(editingId, payload);
      else await adminService.createFaq(payload);
      setForm(null);
      setEditingId(null);
      setNotice('Đã lưu câu FAQ.');
      await load(page);
    } catch (error) {
      setNotice(getApiErrorMessage(error, 'Không lưu được câu FAQ.'));
    } finally {
      setSaving(false);
    }
  };

  const exportJson = async () => {
    try {
      const items = await adminService.exportFaqs();
      const blob = new Blob([JSON.stringify(items, null, 2)], { type: 'application/json' });
      const url = URL.createObjectURL(blob);
      const link = document.createElement('a');
      link.href = url;
      link.download = 'faq-pcstore.json';
      link.click();
      URL.revokeObjectURL(url);
    } catch (error) {
      setNotice(getApiErrorMessage(error, 'Không xuất được JSON.'));
    }
  };

  const importJson = async event => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) return;
    try {
      const items = JSON.parse(await file.text());
      if (!Array.isArray(items)) throw new Error('File phải là một mảng JSON.');
      const result = await adminService.importFaqs(items);
      setNotice(result.message || 'Đã nhập FAQ.');
      await load(1);
    } catch (error) {
      setNotice(error instanceof SyntaxError ? 'File JSON không hợp lệ.' : getApiErrorMessage(error, 'Không nhập được JSON.'));
    }
  };

  const totalPages = Math.max(1, Math.ceil((pageData?.total || 0) / (pageData?.pageSize || 10)));

  return (
    <div className="space-y-4">
      {pageData?.placeholderCount > 0 && (
        <div className="rounded-2xl border border-amber-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
          Có {pageData.placeholderCount} câu còn nội dung [CẦN ĐIỀN]. Các câu này đang tắt và không được trả cho khách.
        </div>
      )}
      {notice && <div className="rounded-2xl border border-blue-100 bg-blue-50 px-4 py-3 text-sm text-blue-800">{notice}</div>}
      <section className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
        <div className="flex flex-wrap items-end gap-3">
          <label className="min-w-48 flex-1 text-xs font-semibold text-slate-500">Tìm kiếm
            <input value={search} onChange={event => setSearch(event.target.value)} className={fieldClass} placeholder="Câu hỏi, câu trả lời hoặc từ khóa" />
          </label>
          <label className="text-xs font-semibold text-slate-500">Nhóm
            <select value={category} onChange={event => setCategory(event.target.value)} className={fieldClass}>
              <option value="">Tất cả</option>
              {(pageData?.categories || []).map(item => <option key={item} value={item}>{item}</option>)}
            </select>
          </label>
          <label className="flex items-center gap-2 pb-2 text-sm text-slate-700">
            <input type="checkbox" checked={needsFill} onChange={event => setNeedsFill(event.target.checked)} />
            Chỉ câu cần điền
          </label>
          <button type="button" onClick={() => load(1)} className="rounded-xl bg-slate-900 px-3 py-2 text-sm font-bold text-white">Lọc</button>
          <button type="button" onClick={() => { setEditingId(null); setForm(emptyForm()); }} className="inline-flex items-center gap-1 rounded-xl border border-slate-300 px-3 py-2 text-sm font-bold text-slate-700"><Plus className="h-4 w-4" /> Thêm</button>
          <button type="button" onClick={exportJson} className="inline-flex items-center gap-1 rounded-xl border border-slate-300 px-3 py-2 text-sm font-bold text-slate-700"><Download className="h-4 w-4" /> Xuất JSON</button>
          <label className="inline-flex cursor-pointer items-center gap-1 rounded-xl border border-slate-300 px-3 py-2 text-sm font-bold text-slate-700">
            <Upload className="h-4 w-4" /> Nhập JSON
            <input type="file" accept="application/json,.json" className="hidden" onChange={importJson} />
          </label>
        </div>
      </section>
      {form && (
        <form onSubmit={save} className="grid gap-3 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm md:grid-cols-2">
          <label className="text-xs font-semibold text-slate-500">Nhóm<input required value={form.category} onChange={event => setForm({ ...form, category: event.target.value })} className={fieldClass} /></label>
          <label className="text-xs font-semibold text-slate-500">Độ ưu tiên<input type="number" min="0" max="1000" value={form.priority} onChange={event => setForm({ ...form, priority: event.target.value })} className={fieldClass} /></label>
          <label className="text-xs font-semibold text-slate-500 md:col-span-2">Câu hỏi<input required value={form.question} onChange={event => setForm({ ...form, question: event.target.value })} className={fieldClass} /></label>
          <label className="text-xs font-semibold text-slate-500 md:col-span-2">Câu trả lời<textarea required rows={4} value={form.answer} onChange={event => setForm({ ...form, answer: event.target.value })} className={fieldClass} /></label>
          <label className="text-xs font-semibold text-slate-500 md:col-span-2">Từ khóa, cách nhau bởi dấu phẩy<input required value={form.keywords} onChange={event => setForm({ ...form, keywords: event.target.value })} className={fieldClass} /></label>
          <label className="flex items-center gap-2 text-sm text-slate-700"><input type="checkbox" checked={form.isActive} onChange={event => setForm({ ...form, isActive: event.target.checked })} />Đang bật</label>
          <div className="flex justify-end gap-2">
            <button type="button" onClick={() => setForm(null)} className="rounded-xl px-3 py-2 text-sm font-bold text-slate-500">Hủy</button>
            <button type="submit" disabled={saving} className="rounded-xl bg-blue-600 px-4 py-2 text-sm font-bold text-white disabled:opacity-60">{saving ? 'Đang lưu...' : 'Lưu'}</button>
          </div>
        </form>
      )}
      <section className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
        {loading ? <div className="py-10 text-center"><Loader2 className="mx-auto h-5 w-5 animate-spin text-blue-600" /></div> : (
          <div className="overflow-x-auto">
            <table className="min-w-full text-left text-sm">
              <thead className="bg-slate-50 text-xs uppercase tracking-wide text-slate-500"><tr><th className="px-3 py-3">Nhóm</th><th className="px-3 py-3">Câu hỏi</th><th className="px-3 py-3">Trạng thái</th><th className="px-3 py-3" /></tr></thead>
              <tbody>
                {(pageData?.items || []).map(item => (
                  <tr key={item.id} className="border-t border-slate-100">
                    <td className="px-3 py-3 align-top font-semibold text-slate-800">{item.category}</td>
                    <td className="px-3 py-3 align-top">
                      <p className="font-medium text-slate-800">{item.question}</p>
                      <p className="mt-1 line-clamp-2 text-xs text-slate-500">{item.answer}</p>
                      {item.needsFill && <p className="mt-1 text-xs font-bold text-amber-700">Còn [CẦN ĐIỀN]</p>}
                    </td>
                    <td className="px-3 py-3 align-top">
                      <button type="button" onClick={async () => { try { await adminService.setFaqActive(item.id, !item.isActive); await load(page); } catch (error) { setNotice(getApiErrorMessage(error, 'Không đổi được trạng thái.')); } }} className={`rounded-full px-2 py-1 text-xs font-bold ${item.isActive ? 'bg-emerald-100 text-emerald-800' : 'bg-slate-100 text-slate-500'}`}>{item.isActive ? 'Đang bật' : 'Đang tắt'}</button>
                    </td>
                    <td className="px-3 py-3 align-top text-right">
                      <button type="button" onClick={() => { setEditingId(item.id); setForm({ category: item.category, question: item.question, answer: item.answer, keywords: item.keywords, priority: item.priority, isActive: item.isActive }); }} className="mr-2 text-xs font-bold text-blue-700">Sửa</button>
                      <button type="button" onClick={async () => { if (!window.confirm('Xóa câu FAQ này?')) return; try { await adminService.deleteFaq(item.id); await load(page); } catch (error) { setNotice(getApiErrorMessage(error, 'Không xóa được câu FAQ.')); } }} className="text-xs font-bold text-rose-600">Xóa</button>
                    </td>
                  </tr>
                ))}
                {(pageData?.items || []).length === 0 && <tr><td colSpan={4} className="px-3 py-8 text-center text-slate-500">Không có câu FAQ khớp bộ lọc.</td></tr>}
              </tbody>
            </table>
          </div>
        )}
        <div className="flex items-center justify-between border-t border-slate-100 px-3 py-3 text-sm text-slate-600">
          <span>{pageData?.total || 0} câu</span>
          <div className="flex gap-2">
            <button type="button" disabled={page <= 1} onClick={() => load(page - 1)} className="rounded-lg border border-slate-200 px-2 py-1 disabled:opacity-40">Trước</button>
            <span>{page}/{totalPages}</span>
            <button type="button" disabled={page >= totalPages} onClick={() => load(page + 1)} className="rounded-lg border border-slate-200 px-2 py-1 disabled:opacity-40">Sau</button>
          </div>
        </div>
      </section>
    </div>
  );
}
