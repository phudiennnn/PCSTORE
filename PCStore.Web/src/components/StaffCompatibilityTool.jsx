import { useEffect, useMemo, useState } from 'react';
import { AlertTriangle, CheckCircle2, Cpu, Loader2, Search, XCircle } from 'lucide-react';
import { getApiErrorMessage, productService, staffService } from '../services/api';

const ISSUE_LABELS = {
  socket: 'Socket CPU / mainboard',
  'ram-type': 'Chuẩn RAM',
  power: 'Công suất nguồn',
  'case-form-factor': 'Kích thước mainboard / case',
  'gpu-length': 'Chiều dài card đồ họa',
  'cooler-socket': 'Socket tản nhiệt / CPU',
  'cooler-height': 'Chiều cao tản nhiệt / case',
};

const formatPrice = value => `${Number(value || 0).toLocaleString('vi-VN')} đ`;

export default function StaffCompatibilityTool() {
  const [products, setProducts] = useState([]);
  const [selectedParts, setSelectedParts] = useState({});
  const [search, setSearch] = useState('');
  const [catalogLoading, setCatalogLoading] = useState(true);
  const [checking, setChecking] = useState(false);
  const [report, setReport] = useState(null);
  const [error, setError] = useState('');

  useEffect(() => {
    let active = true;
    const loadCatalog = async () => {
      try {
        const catalog = await productService.getProducts();
        if (active) setProducts(catalog);
      } catch (loadError) {
        if (active) setError(getApiErrorMessage(loadError, 'Không thể tải danh sách linh kiện. Vui lòng thử lại.'));
      } finally {
        if (active) setCatalogLoading(false);
      }
    };
    void loadCatalog();
    return () => {
      active = false;
    };
  }, []);

  const selectedIds = useMemo(
    () => Object.values(selectedParts).map(product => product.id).sort((a, b) => a - b),
    [selectedParts]
  );
  const selectedIdsKey = selectedIds.join(',');

  useEffect(() => {
    if (!selectedIdsKey) return undefined;

    const controller = new AbortController();
    const timer = window.setTimeout(async () => {
      setChecking(true);
      setError('');
      try {
        const productIds = selectedIdsKey.split(',').map(Number);
        const result = await staffService.checkPcCompatibility(productIds, { signal: controller.signal });
        if (!controller.signal.aborted) setReport(result);
      } catch (checkError) {
        if (!controller.signal.aborted) {
          setReport(null);
          setError(getApiErrorMessage(checkError, 'Không thể kiểm tra tương thích cấu hình.'));
        }
      } finally {
        if (!controller.signal.aborted) setChecking(false);
      }
    }, 180);

    return () => {
      window.clearTimeout(timer);
      controller.abort();
    };
  }, [selectedIdsKey]);

  const filteredProducts = useMemo(() => {
    const term = search.trim().toLocaleLowerCase('vi');
    if (!term) return [];
    return products
      .filter(product => `${product.name} ${product.brand || ''} ${product.sku} ${product.categoryName}`.toLocaleLowerCase('vi').includes(term))
      .slice(0, 30);
  }, [products, search]);

  const addProduct = product => {
    setSelectedParts(current => ({ ...current, [product.categoryType]: product }));
    setSearch('');
  };

  const removeProduct = categoryType => {
    const next = { ...selectedParts };
    delete next[categoryType];
    setSelectedParts(next);
    if (Object.keys(next).length === 0) {
      setChecking(false);
      setReport(null);
    }
  };

  const applyReplacement = suggestion => {
    addProduct(suggestion.product);
  };

  return (
    <section className="space-y-4">
      <div>
        <h3 className="flex items-center gap-2 text-sm font-black text-white">
          <Cpu className="h-4 w-4 text-cyan-400" />
          Hỗ trợ kỹ thuật & kiểm tra tương thích PC
        </h3>
        <p className="mt-1 text-[10px] leading-relaxed text-slate-400">
          Tìm và chọn các linh kiện khách đang quan tâm. Hệ thống tự kiểm tra socket, RAM, nguồn và kích thước case,
          rồi gợi ý linh kiện thay thế còn hàng.
        </p>
      </div>

      <div className="relative">
        <label htmlFor="staff-component-search" className="mb-1 block text-[10px] font-bold text-slate-300">Thêm linh kiện vào cấu hình tư vấn</label>
        <div className="flex items-center gap-2 rounded-xl border border-cyan-950 bg-[#111a2e] px-3">
          <Search className="h-4 w-4 shrink-0 text-slate-500" />
          <input
            id="staff-component-search"
            value={search}
            onChange={event => setSearch(event.target.value)}
            placeholder="Tìm tên, mã SKU hoặc thương hiệu..."
            className="min-w-0 flex-1 bg-transparent py-2.5 text-xs text-white outline-none placeholder:text-slate-500"
          />
          {catalogLoading && <Loader2 className="h-4 w-4 animate-spin text-cyan-400" />}
        </div>
        {search.trim() && (
          <div className="absolute z-20 mt-1 max-h-64 w-full overflow-y-auto rounded-xl border border-cyan-800 bg-[#0c1424] shadow-xl">
            {catalogLoading ? (
              <p className="p-3 text-xs text-slate-400">Đang tải danh sách linh kiện...</p>
            ) : filteredProducts.length === 0 ? (
              <p className="p-3 text-xs text-slate-400">Không tìm thấy linh kiện phù hợp.</p>
            ) : filteredProducts.map(product => (
              <button
                key={product.id}
                type="button"
                onClick={() => addProduct(product)}
                className="flex w-full items-center justify-between gap-3 border-b border-cyan-950 px-3 py-2 text-left last:border-b-0 hover:bg-cyan-950/50"
              >
                <span className="min-w-0">
                  <span className="block truncate text-xs font-bold text-white">{product.name}</span>
                  <span className="block text-[10px] text-slate-400">{product.categoryName} · {product.sku} · {product.brand || 'Không rõ hãng'}</span>
                </span>
                <span className="shrink-0 text-right">
                  <span className="block text-[10px] font-bold text-cyan-300">{formatPrice(product.price)}</span>
                  <span className={`block text-[9px] ${product.stockQuantity > 0 ? 'text-emerald-400' : 'text-rose-400'}`}>
                    {product.stockQuantity > 0 ? `Còn ${product.stockQuantity}` : 'Hết hàng'}
                  </span>
                </span>
              </button>
            ))}
          </div>
        )}
      </div>

      <div className="space-y-2">
        {Object.entries(selectedParts).length === 0 ? (
          <p className="rounded-xl border border-dashed border-cyan-950 p-5 text-center text-xs text-slate-500">
            Chọn linh kiện phía trên để bắt đầu kiểm tra cấu hình.
          </p>
        ) : Object.entries(selectedParts).map(([categoryType, product]) => (
          <div key={categoryType} className="flex items-center justify-between gap-3 rounded-xl border border-cyan-950 bg-[#111a2e] p-3">
            <div className="min-w-0">
              <p className="text-[9px] font-black uppercase text-cyan-400">{product.categoryName || categoryType}</p>
              <p className="truncate text-xs font-bold text-white">{product.name}</p>
              <p className="text-[10px] text-slate-400">{formatPrice(product.price)} · {product.stockQuantity > 0 ? `Còn ${product.stockQuantity}` : 'Hết hàng'}</p>
            </div>
            <button type="button" onClick={() => removeProduct(categoryType)} aria-label={`Xóa ${product.name}`} className="shrink-0 rounded-lg p-2 text-slate-500 hover:bg-rose-950/40 hover:text-rose-300">
              <XCircle className="h-4 w-4" />
            </button>
          </div>
        ))}
      </div>

      {checking && (
        <div role="status" className="flex items-center gap-2 rounded-xl border border-cyan-900 bg-cyan-950/30 p-3 text-xs text-cyan-200">
          <Loader2 className="h-4 w-4 animate-spin" /> Đang quét quy tắc tương thích...
        </div>
      )}
      {error && <p role="alert" className="rounded-xl border border-rose-900 bg-rose-950/40 p-3 text-xs text-rose-300">{error}</p>}

      {selectedIdsKey && report && !checking && (
        <div className="space-y-3">
          <div className={`flex items-start gap-2 rounded-xl border p-3 ${report.isCompatible ? 'border-emerald-800 bg-emerald-950/30 text-emerald-200' : 'border-rose-900 bg-rose-950/30 text-rose-200'}`}>
            {report.isCompatible ? <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0" /> : <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" />}
            <div>
              <p className="text-xs font-black">{report.isCompatible ? 'Cấu hình tương thích' : `Phát hiện ${report.issues.length} xung đột tương thích`}</p>
              <p className="mt-0.5 text-[10px] opacity-80">Hoàn tất trong {Number(report.elapsedMilliseconds).toFixed(1)} ms</p>
            </div>
          </div>

          {report.issues.length > 0 && (
            <div className="space-y-2">
              {report.issues.map(issue => (
                <article key={issue.code} className="rounded-xl border border-rose-900/70 bg-[#111a2e] p-3">
                  <p className="text-[10px] font-black uppercase text-rose-300">{ISSUE_LABELS[issue.code] || issue.code}</p>
                  <p className="mt-1 text-xs leading-relaxed text-slate-200">{issue.message}</p>
                </article>
              ))}
            </div>
          )}

          {report.suggestedReplacements.length > 0 && (
            <div className="space-y-2">
              <h4 className="text-xs font-black text-white">Linh kiện thay thế tương thích, còn hàng</h4>
              {report.suggestedReplacements.map(suggestion => (
                <article key={`${suggestion.replacesCategoryType}-${suggestion.product.id}`} className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-emerald-900/70 bg-emerald-950/15 p-3">
                  <div className="min-w-0">
                    <p className="text-[9px] font-black uppercase text-emerald-300">Thay {suggestion.replacesCategoryType} · còn {suggestion.product.stockQuantity} sản phẩm</p>
                    <p className="truncate text-xs font-bold text-white">{suggestion.product.name}</p>
                    <p className="text-[10px] text-slate-400">
                      {formatPrice(suggestion.product.price)}
                      {' · Chênh '}
                      {suggestion.priceDifference > 0 ? '+' : ''}
                      {formatPrice(suggestion.priceDifference)}
                      {suggestion.remainingIssues.length > 0 && ` · Còn ${suggestion.remainingIssues.length} cảnh báo`}
                    </p>
                  </div>
                  <button type="button" onClick={() => applyReplacement(suggestion)} className="rounded-lg bg-emerald-600 px-3 py-2 text-[10px] font-bold text-white hover:bg-emerald-500">
                    Dùng linh kiện này
                  </button>
                </article>
              ))}
            </div>
          )}
        </div>
      )}
    </section>
  );
}
