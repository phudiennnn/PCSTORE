import { useState } from 'react';
import { ClipboardList, Loader2, Search, UserRound } from 'lucide-react';
import { getApiErrorMessage, staffService } from '../services/api';

const ORDER_STATUS_LABELS = {
  Pending: 'Chờ xử lý',
  Confirmed: 'Đã xác nhận / Đang đóng gói',
  Shipping: 'Đang giao hàng',
  Completed: 'Đã giao thành công',
  Cancelled: 'Đã hủy',
  DeliveryFailed: 'Giao hàng thất bại',
  Refunded: 'Đã hoàn tiền',
};

export default function CustomerLookup() {
  const [search, setSearch] = useState('');
  const [customers, setCustomers] = useState([]);
  const [customerHistory, setCustomerHistory] = useState(null);
  const [loading, setLoading] = useState(false);
  const [detailLoading, setDetailLoading] = useState(false);
  const [error, setError] = useState('');

  const handleSearch = async (event) => {
    event.preventDefault();
    setError('');
    setCustomerHistory(null);
    const term = search.trim();
    if (term.length < 2) {
      setCustomers([]);
      setError('Nhập ít nhất 2 ký tự để tìm; tìm số điện thoại cần ít nhất 4 số cuối.');
      return;
    }

    setLoading(true);
    try {
      setCustomers(await staffService.searchCustomers(term));
    } catch (searchError) {
      setError(getApiErrorMessage(searchError, 'Không thể tra cứu khách hàng.'));
      setCustomers([]);
    } finally {
      setLoading(false);
    }
  };

  const handleSelectCustomer = async (customerId) => {
    setDetailLoading(true);
    setError('');
    try {
      setCustomerHistory(await staffService.getCustomerHistory(customerId));
    } catch (detailError) {
      setError(getApiErrorMessage(detailError, 'Không thể tải lịch sử khách hàng.'));
      setCustomerHistory(null);
    } finally {
      setDetailLoading(false);
    }
  };

  const customer = customerHistory?.customer;

  return (
    <div className="space-y-4">
      <form onSubmit={handleSearch} className="flex gap-2">
        <input
          value={search}
          onChange={event => setSearch(event.target.value)}
          placeholder="SĐT / 4 số cuối, email hoặc mã KH..."
          aria-label="Tìm khách hàng theo số điện thoại, email hoặc mã khách hàng"
          className="min-w-0 flex-1 rounded-xl border border-cyan-950 bg-[#111a2e] px-3 py-2 text-slate-100 placeholder:text-slate-500"
        />
        <button type="submit" disabled={loading} className="flex items-center gap-1 rounded-xl bg-cyan-400 px-4 py-2 font-bold text-slate-950 disabled:opacity-50">
          {loading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Search className="h-4 w-4" />}
          Tìm
        </button>
      </form>
      <p className="text-[10px] text-slate-500">Kết quả tối đa 20 khách hàng. Dùng ít nhất 4 chữ số cuối cho tra cứu SĐT.</p>

      {error && <p role="alert" className="rounded-xl border border-rose-900 bg-rose-950/40 p-3 text-xs text-rose-300">{error}</p>}

      {customers.length > 0 && (
        <div className="grid gap-2 md:grid-cols-2">
          {customers.map(result => (
            <button
              key={result.id}
              type="button"
              onClick={() => handleSelectCustomer(result.id)}
              className={`rounded-xl border p-3 text-left transition ${customer?.id === result.id ? 'border-cyan-400 bg-cyan-950/40' : 'border-cyan-950 bg-[#111a2e] hover:border-cyan-800'}`}
            >
              <span className="flex items-center gap-2 font-bold text-white"><UserRound className="h-4 w-4 text-cyan-400" />{result.fullName}</span>
              <span className="mt-1 block text-[10px] text-slate-400">{result.customerCode} · {result.email}</span>
              <span className="mt-0.5 block text-[10px] text-slate-400">{result.phoneNumber || 'Chưa có SĐT'}</span>
              <span className="mt-1 block text-[10px] text-cyan-300">{result.orderCount} đơn · {result.savedBuildCount} cấu hình đã lưu</span>
            </button>
          ))}
        </div>
      )}
      {!loading && search.trim().length >= 2 && customers.length === 0 && !error && (
        <p className="py-5 text-center text-xs text-slate-500">Không tìm thấy khách hàng phù hợp.</p>
      )}

      {detailLoading && <div className="flex justify-center py-6"><Loader2 className="h-5 w-5 animate-spin text-cyan-400" /></div>}
      {customer && !detailLoading && (
        <section className="space-y-4 border-t border-cyan-950 pt-4">
          <div className="rounded-xl border border-cyan-900 bg-[#111a2e] p-4">
            <div className="flex flex-wrap items-start justify-between gap-3">
              <div>
                <h3 className="text-sm font-black text-white">{customer.fullName}</h3>
                <p className="mt-1 text-[10px] text-slate-400">{customer.customerCode} · Tham gia {new Date(customer.createdAt).toLocaleDateString('vi-VN')}</p>
              </div>
              <p className="text-xs font-bold text-emerald-300">Tổng chi tiêu: {Number(customer.totalSpent).toLocaleString('vi-VN')} đ</p>
            </div>
            <div className="mt-3 grid gap-2 text-[11px] text-slate-300 sm:grid-cols-2">
              <p>Email: <a className="text-cyan-300 hover:underline" href={`mailto:${customer.email}`}>{customer.email}</a></p>
              <p>SĐT: {customer.phoneNumber ? <a className="text-cyan-300 hover:underline" href={`tel:${customer.phoneNumber}`}>{customer.phoneNumber}</a> : 'Chưa có'}</p>
              <p className="sm:col-span-2">Địa chỉ giao hàng: {customer.address || 'Chưa cập nhật'}</p>
            </div>
          </div>

          <div>
            <h4 className="mb-2 flex items-center gap-2 font-bold text-white"><ClipboardList className="h-4 w-4 text-cyan-400" />Lịch sử đơn hàng ({customerHistory.orders.length})</h4>
            {customerHistory.orders.length === 0 ? <p className="rounded-xl bg-[#111a2e] p-3 text-[11px] text-slate-500">Khách hàng chưa có đơn hàng gắn với tài khoản.</p> : (
              <div className="space-y-2">
                {customerHistory.orders.map(order => (
                  <article key={order.id} className="rounded-xl border border-cyan-950 bg-[#111a2e] p-3">
                    <div className="flex flex-wrap justify-between gap-2">
                      <div>
                        <p className="font-bold text-cyan-300">{order.orderCode}</p>
                        <p className="text-[10px] text-slate-500">{new Date(order.createdAt).toLocaleString('vi-VN')}</p>
                      </div>
                      <div className="text-right">
                        <p className="font-bold text-white">{Number(order.totalAmount).toLocaleString('vi-VN')} đ</p>
                        <p className="text-[10px] text-cyan-200">{ORDER_STATUS_LABELS[order.status] || order.status}</p>
                      </div>
                    </div>
                    <div className="mt-2 space-y-1 border-t border-cyan-950 pt-2">
                      {order.items.map((item, index) => (
                        <p key={`${item.productName}-${index}`} className="flex justify-between gap-2 text-[10px] text-slate-300">
                          <span>{item.productName} × {item.quantity}</span>
                          <span className="shrink-0">{(item.unitPrice * item.quantity).toLocaleString('vi-VN')} đ</span>
                        </p>
                      ))}
                    </div>
                    <p className="mt-2 text-[10px] text-slate-400">
                      Giao đến: {order.shippingAddress || 'Chưa có địa chỉ'}
                      {order.receiverPhone ? ` · ${order.receiverPhone}` : ''}
                    </p>
                    {(order.carrier || order.trackingNumber) && (
                      <p className="mt-2 text-[10px] text-slate-400">{order.carrier || 'Vận chuyển'}{order.trackingNumber ? ` · ${order.trackingNumber}` : ''}</p>
                    )}
                    {order.statusHistory.length > 0 && (
                      <details className="mt-2 text-[10px]">
                        <summary className="cursor-pointer text-cyan-400">Lịch sử cập nhật ({order.statusHistory.length})</summary>
                        <ol className="mt-2 space-y-1 border-l border-cyan-900 pl-3">
                          {order.statusHistory.map((entry, index) => (
                            <li key={`${entry.changedAt}-${index}`} className="text-slate-400">
                              {ORDER_STATUS_LABELS[entry.status] || entry.status} · {new Date(entry.changedAt).toLocaleString('vi-VN')}
                              {entry.changedByName ? ` · ${entry.changedByName}` : ''}
                              {entry.note ? ` — ${entry.note}` : ''}
                            </li>
                          ))}
                        </ol>
                      </details>
                    )}
                  </article>
                ))}
              </div>
            )}
          </div>

          <div>
            <h4 className="mb-2 font-bold text-white">Cấu hình PC đã lưu ({customerHistory.builds.length})</h4>
            {customerHistory.builds.length === 0 ? <p className="rounded-xl bg-[#111a2e] p-3 text-[11px] text-slate-500">Khách hàng chưa lưu cấu hình PC.</p> : (
              <div className="space-y-2">
                {customerHistory.builds.map(build => (
                  <details key={build.id} className="rounded-xl border border-cyan-950 bg-[#111a2e] p-3">
                    <summary className="flex cursor-pointer list-none flex-wrap items-center justify-between gap-2">
                      <span>
                        <span className="block font-bold text-white">{build.buildName}</span>
                        <span className="text-[10px] text-slate-500">{new Date(build.createdAt).toLocaleString('vi-VN')} · {build.estimatedWattage}W</span>
                      </span>
                      <span className="font-bold text-cyan-300">{Number(build.totalPrice).toLocaleString('vi-VN')} đ</span>
                    </summary>
                    <div className="mt-3 space-y-1 border-t border-cyan-950 pt-2">
                      {build.items.map((item, index) => (
                        <p key={`${item.productName}-${index}`} className="flex justify-between gap-2 text-[10px] text-slate-300">
                          <span>{item.categoryName} · {item.productName} × {item.quantity}</span>
                          <span className="shrink-0">{(item.unitPrice * item.quantity).toLocaleString('vi-VN')} đ</span>
                        </p>
                      ))}
                    </div>
                  </details>
                ))}
              </div>
            )}
          </div>
        </section>
      )}
    </div>
  );
}
