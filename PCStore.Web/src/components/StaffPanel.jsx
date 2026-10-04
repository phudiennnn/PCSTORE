import { useState, useEffect } from 'react';
import { Package, Users, Headphones, Loader2, X, Search, Printer, Eye, CheckCircle2, ClipboardList, Truck } from 'lucide-react';
import { staffService, orderService, getApiErrorMessage } from '../services/api';

const TABS = [
  { id: 'orders', label: 'Xử lý đơn', icon: Package },
  { id: 'warehouse', label: 'Phiếu xuất kho', icon: ClipboardList },
  { id: 'delivery', label: 'Đang vận chuyển', icon: Truck },
  { id: 'customers', label: 'Tra cứu KH', icon: Users },
  { id: 'support', label: 'Hỗ trợ KT', icon: Headphones },
];

const CARRIERS = ['GHN', 'GHTK', 'Viettel Post', 'J&T Express', 'VNPost', 'Khác'];
const ORDER_STATUS_LABELS = {
  Pending: 'Chờ xử lý',
  Confirmed: 'Đã xác nhận / Đang đóng gói',
  Shipping: 'Đã bàn giao vận chuyển / Đang giao hàng',
  Completed: 'Đã giao thành công',
  Cancelled: 'Đã hủy',
  DeliveryFailed: 'Giao hàng thất bại',
};

export default function StaffPanel({ onClose, staffName = 'Nhân viên' }) {
  const [tab, setTab] = useState('orders');
  const [loading, setLoading] = useState(false);
  const [orders, setOrders] = useState([]);
  const [customers, setCustomers] = useState([]);
  const [tickets, setTickets] = useState([]);
  const [search, setSearch] = useState('');
  const [msg, setMsg] = useState('');
  const [selectedOrder, setSelectedOrder] = useState(null);
  const [expandedOrderId, setExpandedOrderId] = useState(null);
  const [confirmingOrderId, setConfirmingOrderId] = useState(null);
  const [updatingOrderId, setUpdatingOrderId] = useState(null);
  const [statusDrafts, setStatusDrafts] = useState({});

  const loadTab = async (t) => {
    setLoading(true);
    try {
      if (t === 'orders') setOrders(await orderService.getAllOrders('Pending'));
      if (t === 'warehouse') setOrders(await orderService.getAllOrders('Confirmed'));
      if (t === 'delivery') setOrders(await orderService.getAllOrders('Shipping'));
      if (t === 'customers') setCustomers(await staffService.searchCustomers(search));
      if (t === 'support') setTickets(await staffService.getSupportTickets());
    } catch { setMsg('Lỗi tải dữ liệu.'); }
    finally { setLoading(false); }
  };

  // eslint-disable-next-line react-hooks/exhaustive-deps
  useEffect(() => { const run = async () => { await loadTab(tab); }; run(); }, [tab]);

  useEffect(() => {
    if (!['orders', 'warehouse', 'delivery'].includes(tab)) return undefined;
    let active = true;
    let connection;
    const connect = async () => {
      try {
        const connected = await orderService.connectToNewOrderNotifications(async () => {
          if (!active || tab !== 'orders') return;
          setMsg('Có đơn hàng mới cần xử lý.');
          try {
            setOrders(await orderService.getAllOrders('Pending'));
          } catch {
            setMsg('Có đơn hàng mới, nhưng không tải lại được danh sách.');
          }
        }, async () => {
          if (!active) return;
          setMsg('Đơn hàng đã được xác nhận và chuyển vào hàng đợi xuất kho.');
          try {
            const status = tab === 'warehouse' ? 'Confirmed' : tab === 'delivery' ? 'Shipping' : 'Pending';
            setOrders(await orderService.getAllOrders(status));
          } catch {
            setMsg('Đơn đã xác nhận nhưng không tải lại được danh sách.');
          }
        }, async () => {
          if (!active) return;
          try {
            const status = tab === 'warehouse' ? 'Confirmed' : tab === 'delivery' ? 'Shipping' : 'Pending';
            setOrders(await orderService.getAllOrders(status));
          } catch {
            setMsg('Đơn hàng đã đổi trạng thái nhưng không tải lại được danh sách.');
          }
        });
        if (!active) {
          await connected.stop();
          return;
        }
        connection = connected;
      } catch {
        if (active) setMsg('Không kết nối được thông báo đơn hàng realtime. Hãy làm mới danh sách để kiểm tra đơn mới.');
      }
    };
    connect();
    return () => {
      active = false;
      connection?.stop();
    };
  }, [tab]);

  const handleSearch = async (e) => {
    e.preventDefault();
    setCustomers(await staffService.searchCustomers(search));
  };

  const handleAcceptOrder = async (id) => {
    setConfirmingOrderId(id);
    let confirmed = false;
    try {
      const result = await orderService.confirmOrder(id, staffName);
      confirmed = true;
      setOrders(current => current.filter(order => order.id !== id));
      setMsg(result.emailWarning
        ? `Đã xác nhận đơn hàng. ${result.emailWarning}`
        : 'Đã xác nhận đơn hàng; tồn kho đã được kiểm tra và đặt trước.');
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể xác nhận đơn hàng.'));
    } finally {
      setConfirmingOrderId(null);
    }
    if (confirmed) {
      try {
        setOrders(await orderService.getAllOrders('Pending'));
      } catch (error) {
        setMsg(`Đã xác nhận đơn hàng, nhưng không tải lại được danh sách: ${getApiErrorMessage(error, 'lỗi tải dữ liệu')}`);
      }
    }
  };

  const setDraftValue = (orderId, field, value) => {
    setStatusDrafts(current => ({
      ...current,
      [orderId]: { ...current[orderId], [field]: value }
    }));
  };

  const handleUpdateOrderStatus = async (order, status) => {
    const draft = statusDrafts[order.id] || {};
    setUpdatingOrderId(order.id);
    let updated = false;
    try {
      const result = await orderService.updateStatus(order.id, status, {
        trackingNumber: draft.trackingNumber,
        carrier: draft.carrier,
        note: draft.note,
        changedByName: staffName
      });
      updated = true;
      setOrders(current => current.filter(item => item.id !== order.id));
      setMsg(result.emailWarning
        ? `Đã cập nhật trạng thái. ${result.emailWarning}`
        : `Đã cập nhật trạng thái đơn ${order.orderCode}.`);
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể cập nhật trạng thái. Vui lòng tải lại đơn và thử lại.'));
    } finally {
      setUpdatingOrderId(null);
    }
    if (updated) {
      try {
        const queryStatus = tab === 'warehouse' ? 'Confirmed' : tab === 'delivery' ? 'Shipping' : 'Pending';
        setOrders(await orderService.getAllOrders(queryStatus));
      } catch (error) {
        setMsg(`Đã cập nhật trạng thái đơn ${order.orderCode}, nhưng không tải lại được danh sách: ${getApiErrorMessage(error, 'lỗi tải dữ liệu')}`);
      }
    }
  };

  const handlePrint = async (orderId) => {
    const order = await orderService.getOrderById(orderId);
    setSelectedOrder(order);
    setTimeout(() => window.print(), 300);
  };

  return (
    <div className="fixed inset-0 z-50 bg-black/90 backdrop-blur-md flex items-center justify-center p-2 sm:p-4">
      <div className="bg-[#0c1424] border border-cyan-900 rounded-3xl w-full max-w-4xl max-h-[90vh] flex flex-col shadow-2xl overflow-hidden">
        <div className="p-4 border-b border-cyan-950 flex justify-between items-center bg-[#090e1a]">
          <h2 className="text-sm font-black text-white uppercase tracking-wider">Nhân Viên - Xử Lý Đơn Hàng</h2>
          <button onClick={onClose} className="text-slate-500 hover:text-white"><X className="w-5 h-5" /></button>
        </div>

        <div className="flex border-b border-cyan-950">
          {TABS.map(t => (
            <button key={t.id} onClick={() => setTab(t.id)}
              className={`flex items-center gap-1.5 px-4 py-2.5 text-xs font-bold transition ${tab === t.id ? 'text-cyan-400 border-b-2 border-cyan-400' : 'text-slate-400 hover:text-white'}`}>
              <t.icon className="w-3.5 h-3.5" />{t.label}
            </button>
          ))}
        </div>

        <div className="p-4 overflow-y-auto flex-1 text-xs text-slate-200">
          {msg && <div role="status" className="mb-3 p-2 bg-cyan-950/50 border border-cyan-800 text-cyan-200 rounded-xl">{msg}</div>}
          {loading ? (
            <div className="py-12 text-center"><Loader2 className="w-6 h-6 animate-spin mx-auto text-cyan-400" /></div>
          ) : (
            <>
              {['orders', 'warehouse', 'delivery'].includes(tab) && (
                <div className="space-y-2">
                  {orders.length === 0 ? <p className="text-slate-500 text-center py-8">{
                    tab === 'warehouse' ? 'Chưa có phiếu xuất kho chờ xử lý.'
                      : tab === 'delivery' ? 'Không có đơn đang vận chuyển.'
                        : 'Không có đơn chờ xử lý.'
                  }</p> : orders.map(o => (
                    <div key={o.id} className="bg-[#111a2e] border border-cyan-950 rounded-xl p-3">
                      <div className="flex justify-between items-start mb-2">
                        <div>
                          <span className="font-bold text-cyan-400">{o.orderCode}</span>
                          <p className="text-slate-400">{o.receiverName} • {o.receiverPhone}</p>
                          <p className="text-white font-bold">{o.totalAmount?.toLocaleString('vi-VN')}đ • {o.itemCount} SP</p>
                        </div>
                        <span className="px-2 py-0.5 rounded bg-amber-950 text-amber-300 text-[10px]">{ORDER_STATUS_LABELS[o.status] || o.status}</span>
                      </div>
                      {tab === 'orders' && <div className="flex gap-1 flex-wrap">
                        <button
                          onClick={() => handleAcceptOrder(o.id)}
                          disabled={confirmingOrderId === o.id}
                          className="px-3 py-1 bg-emerald-600 text-white rounded font-bold disabled:opacity-50 flex items-center gap-1"
                        >
                          <CheckCircle2 className="w-3 h-3" />
                          {confirmingOrderId === o.id ? 'Đang kiểm tra kho...' : 'Xác nhận đơn hàng'}
                        </button>
                        <button
                          onClick={() => handleUpdateOrderStatus(o, 'Cancelled')}
                          disabled={updatingOrderId === o.id}
                          className="px-3 py-1 bg-rose-700 text-white rounded disabled:opacity-50"
                        >
                          Hủy đơn
                        </button>
                        <button
                          onClick={() => setExpandedOrderId(current => current === o.id ? null : o.id)}
                          className="px-3 py-1 bg-slate-700 text-white rounded flex items-center gap-1"
                        >
                          <Eye className="w-3 h-3" />{expandedOrderId === o.id ? 'Ẩn chi tiết' : 'Xem chi tiết'}
                        </button>
                        <button onClick={() => handlePrint(o.id)} className="px-3 py-1 bg-indigo-950 border border-indigo-800 rounded flex items-center gap-1"><Printer className="w-3 h-3" /> In phiếu</button>
                      </div>}
                      {expandedOrderId === o.id && (
                        <div className="mt-3 border-t border-cyan-950 pt-3 text-[11px]">
                          <p className="text-slate-400">Địa chỉ giao: <span className="text-slate-200">{o.shippingAddress}</span></p>
                          <p className="mt-1 text-slate-400">Email: <span className="text-slate-200">{o.customerEmail || 'Không có'}</span></p>
                          <div className="mt-2 divide-y divide-cyan-950">
                            {o.items?.map(item => (
                              <div key={item.productId} className="flex justify-between gap-3 py-2">
                                <span className="text-slate-200">{item.productName} <span className="text-slate-500">({item.sku})</span> × {item.quantity}</span>
                                <span className="shrink-0 text-slate-300">{(item.unitPrice * item.quantity).toLocaleString('vi-VN')}đ</span>
                              </div>
                            ))}
                          </div>
                          {o.statusHistory?.length > 0 && (
                            <ol className="mt-3 space-y-2 border-t border-cyan-950 pt-3">
                              {o.statusHistory.map((history, index) => (
                                <li key={`${history.status}-${history.changedAt}-${index}`} className="text-[10px] text-slate-400">
                                  <span className="font-bold text-slate-200">{ORDER_STATUS_LABELS[history.status] || history.status}</span>
                                  {' · '}{new Date(history.changedAt).toLocaleString('vi-VN')}
                                  {history.changedByName && ` · ${history.changedByName}`}
                                  {history.carrier && ` · ${history.carrier}`}
                                  {history.trackingNumber && ` · ${history.trackingNumber}`}
                                  {history.note && <p className="mt-0.5 text-slate-500">Ghi chú: {history.note}</p>}
                                </li>
                              ))}
                            </ol>
                          )}
                        </div>
                      )}
                      {tab === 'warehouse' && (
                        <div className="mt-3 border-t border-cyan-950 pt-3">
                          <p className="mb-2 text-[10px] font-bold uppercase text-emerald-300">Đơn đã xác nhận · Sẵn sàng soạn hàng</p>
                          <div className="divide-y divide-cyan-950">
                            {o.items?.map(item => (
                              <div key={item.productId} className="flex justify-between gap-3 py-2 text-[11px]">
                                <span className="text-slate-200">{item.productName} <span className="text-slate-500">({item.sku})</span></span>
                                <span className="shrink-0 text-slate-300">SL {item.quantity}</span>
                              </div>
                            ))}
                          </div>
                          <button onClick={() => handlePrint(o.id)} className="mt-2 px-3 py-1.5 bg-indigo-950 border border-indigo-800 rounded flex items-center gap-1 text-[11px]">
                            <Printer className="w-3 h-3" /> In phiếu xuất kho
                          </button>
                          <div className="mt-3 grid grid-cols-1 gap-2 sm:grid-cols-2">
                            <select
                              value={statusDrafts[o.id]?.carrier || ''}
                              onChange={event => setDraftValue(o.id, 'carrier', event.target.value)}
                              aria-label={`Đơn vị vận chuyển đơn ${o.orderCode}`}
                              className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-2 text-[11px] text-slate-200"
                            >
                              <option value="">Chọn đơn vị vận chuyển *</option>
                              {CARRIERS.map(carrier => <option key={carrier} value={carrier}>{carrier}</option>)}
                            </select>
                            <input
                              value={statusDrafts[o.id]?.trackingNumber || ''}
                              onChange={event => setDraftValue(o.id, 'trackingNumber', event.target.value)}
                              maxLength={100}
                              placeholder="Mã vận đơn *"
                              aria-label={`Mã vận đơn đơn ${o.orderCode}`}
                              className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-2 text-[11px] text-slate-200"
                            />
                            <input
                              value={statusDrafts[o.id]?.note || ''}
                              onChange={event => setDraftValue(o.id, 'note', event.target.value)}
                              maxLength={500}
                              placeholder="Ghi chú (không bắt buộc)"
                              className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-2 text-[11px] text-slate-200 sm:col-span-2"
                            />
                            <div className="flex gap-2 sm:col-span-2">
                              <button
                                onClick={() => handleUpdateOrderStatus(o, 'Shipping')}
                                disabled={updatingOrderId === o.id || !statusDrafts[o.id]?.carrier || !statusDrafts[o.id]?.trackingNumber?.trim()}
                                className="rounded bg-blue-600 px-3 py-2 text-[11px] font-bold text-white disabled:opacity-50"
                              >
                                {updatingOrderId === o.id ? 'Đang cập nhật...' : 'Bàn giao vận chuyển'}
                              </button>
                              <button
                                onClick={() => handleUpdateOrderStatus(o, 'Cancelled')}
                                disabled={updatingOrderId === o.id}
                                className="rounded bg-rose-700 px-3 py-2 text-[11px] font-bold text-white disabled:opacity-50"
                              >
                                Hủy đơn
                              </button>
                            </div>
                          </div>
                        </div>
                      )}
                      {tab === 'delivery' && (
                        <div className="mt-3 border-t border-cyan-950 pt-3">
                          <p className="text-[11px] font-bold text-blue-300">
                            {[...(o.statusHistory || [])].reverse().find(entry => entry.status === 'Shipping')?.carrier || 'Đơn vị vận chuyển'}
                            {' · Mã vận đơn: '}
                            {[...(o.statusHistory || [])].reverse().find(entry => entry.status === 'Shipping')?.trackingNumber || '—'}
                          </p>
                          <input
                            value={statusDrafts[o.id]?.note || ''}
                            onChange={event => setDraftValue(o.id, 'note', event.target.value)}
                            maxLength={500}
                            placeholder="Ghi chú cập nhật (không bắt buộc)"
                            className="mt-2 w-full rounded border border-cyan-950 bg-[#0c1424] px-2 py-2 text-[11px] text-slate-200"
                          />
                          <div className="mt-2 flex flex-wrap gap-2">
                            <button
                              onClick={() => handleUpdateOrderStatus(o, 'Completed')}
                              disabled={updatingOrderId === o.id}
                              className="rounded bg-emerald-600 px-3 py-2 text-[11px] font-bold text-white disabled:opacity-50"
                            >
                              Xác nhận giao thành công
                            </button>
                            <button
                              onClick={() => handleUpdateOrderStatus(o, 'DeliveryFailed')}
                              disabled={updatingOrderId === o.id}
                              className="rounded bg-rose-700 px-3 py-2 text-[11px] font-bold text-white disabled:opacity-50"
                            >
                              Giao thất bại · Hoàn kho
                            </button>
                          </div>
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              )}

              {tab === 'customers' && (
                <div className="space-y-3">
                  <form onSubmit={handleSearch} className="flex gap-2">
                    <input value={search} onChange={e => setSearch(e.target.value)} placeholder="Tìm theo tên, email, SĐT..."
                      className="flex-1 bg-[#111a2e] border border-cyan-950 rounded-xl px-3 py-2" />
                    <button type="submit" className="px-4 py-2 bg-cyan-400 text-slate-950 font-bold rounded-xl flex items-center gap-1"><Search className="w-4 h-4" /> Tìm</button>
                  </form>
                  {customers.map(c => (
                    <div key={c.id} className="bg-[#111a2e] border border-cyan-950 rounded-xl p-3">
                      <p className="font-bold text-white">{c.fullName}</p>
                      <p className="text-slate-400">{c.email} • {c.phoneNumber}</p>
                      <p className="text-slate-500">{c.address}</p>
                      <p className="text-cyan-400 mt-1">{c.orderCount} đơn hàng</p>
                    </div>
                  ))}
                </div>
              )}

              {tab === 'support' && (
                <div className="space-y-2">
                  {tickets.map(t => (
                    <div key={t.id} className="bg-[#111a2e] border border-cyan-950 rounded-xl p-3 flex justify-between">
                      <div>
                        <p className="font-bold">{t.subject}</p>
                        <p className="text-slate-400">{t.customer}</p>
                      </div>
                      <span className={`px-2 py-0.5 rounded text-[10px] ${t.status === 'Open' ? 'bg-amber-950 text-amber-300' : 'bg-emerald-950 text-emerald-300'}`}>{t.status}</span>
                    </div>
                  ))}
                </div>
              )}
            </>
          )}
        </div>
      </div>

      {selectedOrder && (
        <div className="hidden print:block fixed inset-0 bg-white text-black p-8 z-[9999]">
          <h1 className="text-xl font-bold mb-4">HÓA ĐƠN / PHIẾU ĐÓNG GÓI</h1>
          <p>Mã đơn: {selectedOrder.orderCode}</p>
          <p>Khách: {selectedOrder.receiverName} • {selectedOrder.receiverPhone}</p>
          <p>Địa chỉ: {selectedOrder.shippingAddress}</p>
          <table className="w-full mt-4 border-collapse">
            <thead><tr className="border-b"><th className="text-left p-2">Sản phẩm</th><th className="p-2">SL</th><th className="p-2">Đơn giá</th></tr></thead>
            <tbody>
              {selectedOrder.items?.map((item, i) => (
                <tr key={i} className="border-b"><td className="p-2">{item.productName}</td><td className="p-2 text-center">{item.quantity}</td><td className="p-2 text-right">{item.unitPrice?.toLocaleString('vi-VN')}đ</td></tr>
              ))}
            </tbody>
          </table>
          <p className="mt-4 font-bold text-right">Tổng: {selectedOrder.totalAmount?.toLocaleString('vi-VN')}đ</p>
        </div>
      )}
    </div>
  );
}
