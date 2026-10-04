import { useState, useEffect } from 'react';
import { BarChart3, Users, Package, FolderTree, Settings, Database, Loader2, X, Save, Trash2 } from 'lucide-react';
import { adminService, productService, orderService, getApiErrorMessage } from '../services/api';

const TABS = [
  { id: 'stats', label: 'Báo cáo', icon: BarChart3 },
  { id: 'orders', label: 'Đơn hàng', icon: Package },
  { id: 'products', label: 'Sản phẩm', icon: Package },
  { id: 'categories', label: 'Danh mục', icon: FolderTree },
  { id: 'users', label: 'Tài khoản', icon: Users },
  { id: 'settings', label: 'Cấu hình', icon: Settings },
  { id: 'backup', label: 'Sao lưu', icon: Database },
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

export default function AdminPanel({ onClose, staffName = 'Nhân viên' }) {
  const [tab, setTab] = useState('stats');
  const [loading, setLoading] = useState(false);
  const [stats, setStats] = useState(null);
  const [users, setUsers] = useState([]);
  const [products, setProducts] = useState([]);
  const [categories, setCategories] = useState([]);
  const [orders, setOrders] = useState([]);
  const [settings, setSettings] = useState([]);
  const [backupData, setBackupData] = useState(null);
  const [msg, setMsg] = useState('');
  const [trackingNumbers, setTrackingNumbers] = useState({});
  const [carriers, setCarriers] = useState({});
  const [orderNotes, setOrderNotes] = useState({});
  const [confirmingOrderId, setConfirmingOrderId] = useState(null);

  const [newProduct, setNewProduct] = useState({ name: '', sku: '', brand: '', price: 0, stockQuantity: 0, categoryId: 1 });
  const [newCategory, setNewCategory] = useState({ name: '', componentType: 'CPU', description: '' });

  const loadTab = async (t) => {
    setLoading(true);
    setMsg('');
    try {
      if (t === 'stats') setStats(await adminService.getStats());
      if (t === 'users') setUsers(await adminService.getUsers());
      if (t === 'products') { setProducts(await productService.getProducts()); setCategories(await productService.getCategories()); }
      if (t === 'categories') setCategories(await productService.getCategories());
      if (t === 'orders') setOrders(await orderService.getAllOrders());
      if (t === 'settings') setSettings(await adminService.getSettings());
    } catch { setMsg('Lỗi tải dữ liệu.'); }
    finally { setLoading(false); }
  };

  useEffect(() => { const run = async () => { await loadTab(tab); }; run(); }, [tab]);

  useEffect(() => {
    if (tab !== 'orders') return undefined;
    let active = true;
    let connection;
    const connect = async () => {
      try {
        const connected = await orderService.connectToNewOrderNotifications(async () => {
          if (!active) return;
          setMsg('Có đơn hàng mới.');
          try {
            setOrders(await orderService.getAllOrders());
          } catch {
            setMsg('Có đơn hàng mới, nhưng không tải lại được danh sách.');
          }
        }, async () => {
          if (!active) return;
          setMsg('Đơn hàng đã được xác nhận; phiếu xuất đã chuyển cho bộ phận kho.');
          try {
            setOrders(await orderService.getAllOrders());
          } catch {
            setMsg('Đơn đã xác nhận nhưng không tải lại được danh sách.');
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

  const handleUpdateOrderStatus = async (id, status, trackingNumber = '') => {
    let updated = false;
    if (status === 'Confirmed') {
      setConfirmingOrderId(id);
      try {
        const result = await orderService.confirmOrder(id, staffName);
        updated = true;
        setOrders(current => current.filter(order => order.id !== id));
        setMsg(result.emailWarning
          ? `Đơn hàng đã được xác nhận. ${result.emailWarning}`
          : 'Đơn hàng đã được xác nhận; tồn kho đã được kiểm tra và đặt trước.');
      } catch (error) {
        setMsg(getApiErrorMessage(error, 'Không thể xác nhận đơn hàng.'));
      } finally {
        setConfirmingOrderId(null);
      }
    } else {
      try {
        const result = await orderService.updateStatus(id, status, {
          trackingNumber: status === 'Shipping' ? trackingNumber : undefined,
          carrier: status === 'Shipping' ? carriers[id] : undefined,
          note: orderNotes[id],
          changedByName: staffName
        });
        updated = true;
        setOrders(current => current.filter(order => order.id !== id));
        setMsg(result.emailWarning ? `Đã cập nhật trạng thái. ${result.emailWarning}` : 'Cập nhật trạng thái thành công!');
      } catch (error) {
        setMsg(getApiErrorMessage(error, 'Không thể cập nhật trạng thái đơn hàng.'));
      }
    }
    if (updated) {
      try {
        setOrders(await orderService.getAllOrders());
      } catch (error) {
        setMsg(`Trạng thái đã cập nhật, nhưng danh sách chưa thể làm mới: ${getApiErrorMessage(error, 'lỗi tải dữ liệu')}`);
      }
    }
  };

  const handleUpdateRole = async (id, role) => {
    await adminService.updateUserRole(id, role);
    setMsg('Cập nhật vai trò thành công!');
    setUsers(await adminService.getUsers());
  };

  const handleToggleActive = async (id) => {
    await adminService.toggleUserActive(id);
    setUsers(await adminService.getUsers());
  };

  const handleCreateProduct = async (e) => {
    e.preventDefault();
    await adminService.createProduct(newProduct);
    setMsg('Thêm sản phẩm thành công!');
    setProducts(await productService.getProducts());
  };

  const handleCreateCategory = async (e) => {
    e.preventDefault();
    await adminService.createCategory(newCategory);
    setMsg('Thêm danh mục thành công!');
    setCategories(await productService.getCategories());
  };

  const handleSaveSettings = async () => {
    await adminService.updateSettings(settings.map(s => ({ key: s.key, value: s.value })));
    setMsg('Lưu cấu hình thành công!');
  };

  const handleBackup = async () => {
    const data = await adminService.backup();
    setBackupData(data);
    setMsg('Sao lưu thành công!');
  };

  const handlePrintInvoice = (orderId) => {
    window.open(`${window.location.origin}?printOrder=${orderId}`, '_blank');
  };

  return (
    <div className="fixed inset-0 z-50 bg-black/90 backdrop-blur-md flex items-center justify-center p-2 sm:p-4">
      <div className="bg-[#0c1424] border border-cyan-900 rounded-3xl w-full max-w-5xl max-h-[90vh] flex flex-col shadow-2xl overflow-hidden">
        <div className="p-4 border-b border-cyan-950 flex justify-between items-center bg-[#090e1a]">
          <h2 className="text-sm font-black text-white uppercase tracking-wider">Quản Trị Hệ Thống (Admin)</h2>
          <button onClick={onClose} className="text-slate-500 hover:text-white"><X className="w-5 h-5" /></button>
        </div>

        <div className="flex border-b border-cyan-950 overflow-x-auto">
          {TABS.map(t => (
            <button key={t.id} onClick={() => setTab(t.id)}
              className={`flex items-center gap-1.5 px-4 py-2.5 text-xs font-bold whitespace-nowrap transition ${tab === t.id ? 'text-cyan-400 border-b-2 border-cyan-400' : 'text-slate-400 hover:text-white'}`}>
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
              {tab === 'stats' && stats && (
                <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
                  {[
                    { label: 'Doanh thu', value: `${stats.totalRevenue?.toLocaleString('vi-VN')}đ`, color: 'text-cyan-400' },
                    { label: 'Đơn hàng', value: stats.orderCount, color: 'text-white' },
                    { label: 'Sản phẩm', value: stats.productCount, color: 'text-white' },
                    { label: 'Khách hàng', value: stats.customerCount, color: 'text-white' },
                    { label: 'Chờ xử lý', value: stats.pendingOrders, color: 'text-amber-400' },
                  ].map(s => (
                    <div key={s.label} className="bg-[#111a2e] border border-cyan-950 rounded-xl p-4">
                      <p className="text-slate-500 text-[10px] uppercase font-bold">{s.label}</p>
                      <p className={`text-lg font-black ${s.color}`}>{s.value}</p>
                    </div>
                  ))}
                </div>
              )}

              {tab === 'orders' && (
                <div className="space-y-2">
                  {orders.map(o => (
                    <div key={o.id} className="bg-[#111a2e] border border-cyan-950 rounded-xl p-3 space-y-2">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <div>
                          <span className="font-bold text-cyan-400">{o.orderCode}</span>
                          <span className="text-slate-500 ml-2">{o.receiverName} • {o.totalAmount?.toLocaleString('vi-VN')}đ</span>
                          <span className="ml-2 px-2 py-0.5 rounded bg-cyan-950 text-cyan-300 text-[10px]">{ORDER_STATUS_LABELS[o.status] || o.status}</span>
                        </div>
                        <div className="flex gap-1">
                          {(o.status === 'Pending'
                            ? ['Confirmed']
                            : o.status === 'Confirmed'
                              ? ['Shipping', 'Cancelled']
                              : o.status === 'Shipping'
                                ? ['Completed', 'DeliveryFailed']
                                : []).map(st => (
                            <button
                              key={st}
                              onClick={() => handleUpdateOrderStatus(o.id, st, trackingNumbers[o.id])}
                              disabled={(st === 'Confirmed' && confirmingOrderId === o.id)
                                || (st === 'Shipping' && (!carriers[o.id] || !trackingNumbers[o.id]?.trim()))}
                              className="px-2 py-1 bg-cyan-950 border border-cyan-800 rounded text-[10px] hover:border-cyan-400 disabled:opacity-50"
                            >
                              {st === 'Confirmed'
                                ? (confirmingOrderId === o.id ? 'Đang kiểm kho...' : 'Xác nhận đơn hàng')
                                : st === 'Shipping'
                                  ? 'Bàn giao vận chuyển'
                                  : st === 'Completed'
                                    ? 'Giao thành công'
                                    : st === 'DeliveryFailed'
                                      ? 'Giao thất bại · Hoàn kho'
                                      : 'Hủy đơn'}
                            </button>
                          ))}
                          <button onClick={() => handlePrintInvoice(o.id)} className="px-2 py-1 bg-indigo-950 border border-indigo-800 rounded text-[10px]">In HĐ</button>
                        </div>
                      </div>
                      <div className="border-t border-cyan-950 pt-2">
                        <p className="text-[10px] text-slate-400">Giao đến: {o.shippingAddress} • {o.receiverPhone}</p>
                        <div className="mt-1 flex flex-wrap gap-x-3 gap-y-1 text-[10px] text-slate-300">
                          {o.items?.map(item => (
                            <span key={item.productId}>{item.productName} × {item.quantity}</span>
                          ))}
                        </div>
                        {o.statusHistory?.length > 0 && (
                          <ol className="mt-2 space-y-1 border-t border-cyan-950 pt-2">
                            {o.statusHistory.map((history, index) => (
                              <li key={`${history.status}-${history.changedAt}-${index}`} className="text-[10px] text-slate-400">
                                <span className="font-bold text-slate-200">{ORDER_STATUS_LABELS[history.status] || history.status}</span>
                                {' · '}{new Date(history.changedAt).toLocaleString('vi-VN')}
                                {history.changedByName && ` · ${history.changedByName}`}
                                {history.carrier && ` · ${history.carrier}`}
                                {history.trackingNumber && ` · ${history.trackingNumber}`}
                                {history.note && <p className="mt-0.5">Ghi chú: {history.note}</p>}
                              </li>
                            ))}
                          </ol>
                        )}
                      </div>
                      {o.status === 'Confirmed' && (
                        <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                          <select
                            value={carriers[o.id] || ''}
                            onChange={event => setCarriers(current => ({ ...current, [o.id]: event.target.value }))}
                            aria-label={`Đơn vị vận chuyển đơn ${o.orderCode}`}
                            className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5 text-[10px] text-slate-200"
                          >
                            <option value="">Chọn đơn vị vận chuyển *</option>
                            {CARRIERS.map(carrier => <option key={carrier} value={carrier}>{carrier}</option>)}
                          </select>
                          <input
                            value={trackingNumbers[o.id] || ''}
                            onChange={event => setTrackingNumbers(current => ({ ...current, [o.id]: event.target.value }))}
                            maxLength={100}
                            placeholder="Mã vận đơn *"
                            aria-label={`Mã vận đơn đơn hàng ${o.orderCode}`}
                            className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5 text-[10px] text-slate-200 placeholder:text-slate-500"
                          />
                        </div>
                      )}
                      <input
                        value={orderNotes[o.id] || ''}
                        onChange={event => setOrderNotes(current => ({ ...current, [o.id]: event.target.value }))}
                        maxLength={500}
                        placeholder="Ghi chú cập nhật (không bắt buộc)"
                        aria-label={`Ghi chú đơn hàng ${o.orderCode}`}
                        className="w-full rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5 text-[10px] text-slate-200 placeholder:text-slate-500"
                      />
                    </div>
                  ))}
                </div>
              )}

              {tab === 'products' && (
                <div className="space-y-4">
                  <form onSubmit={handleCreateProduct} className="grid grid-cols-2 md:grid-cols-4 gap-2 bg-[#111a2e] p-3 rounded-xl border border-cyan-950">
                    <input placeholder="Tên SP" value={newProduct.name} onChange={e => setNewProduct({...newProduct, name: e.target.value})} className="bg-[#0c1424] border border-cyan-950 rounded px-2 py-1.5" required />
                    <input placeholder="SKU" value={newProduct.sku} onChange={e => setNewProduct({...newProduct, sku: e.target.value})} className="bg-[#0c1424] border border-cyan-950 rounded px-2 py-1.5" required />
                    <input placeholder="Giá" type="number" value={newProduct.price} onChange={e => setNewProduct({...newProduct, price: +e.target.value})} className="bg-[#0c1424] border border-cyan-950 rounded px-2 py-1.5" />
                    <button type="submit" className="bg-cyan-400 text-slate-950 font-bold rounded py-1.5">Thêm SP</button>
                  </form>
                  {products.map(p => (
                    <div key={p.id} className="flex items-center justify-between bg-[#111a2e] border border-cyan-950 rounded-xl p-3">
                      <span className="font-bold truncate flex-1">{p.name}</span>
                      <span className="text-cyan-400 mx-2">{p.price?.toLocaleString('vi-VN')}đ</span>
                      <span className="text-slate-500 mr-2">Kho: {p.stockQuantity}</span>
                      <button onClick={async () => { await adminService.deleteProduct(p.id); setProducts(await productService.getProducts()); }} className="text-rose-400"><Trash2 className="w-4 h-4" /></button>
                    </div>
                  ))}
                </div>
              )}

              {tab === 'categories' && (
                <div className="space-y-4">
                  <form onSubmit={handleCreateCategory} className="grid grid-cols-3 gap-2 bg-[#111a2e] p-3 rounded-xl border border-cyan-950">
                    <input placeholder="Tên danh mục" value={newCategory.name} onChange={e => setNewCategory({...newCategory, name: e.target.value})} className="bg-[#0c1424] border border-cyan-950 rounded px-2 py-1.5" required />
                    <select value={newCategory.componentType} onChange={e => setNewCategory({...newCategory, componentType: e.target.value})} className="bg-[#0c1424] border border-cyan-950 rounded px-2 py-1.5">
                      {['CPU','Mainboard','RAM','GPU','SSD','PSU','Case','Cooler'].map(t => <option key={t} value={t}>{t}</option>)}
                    </select>
                    <button type="submit" className="bg-cyan-400 text-slate-950 font-bold rounded py-1.5">Thêm DM</button>
                  </form>
                  {categories.map(c => (
                    <div key={c.id} className="bg-[#111a2e] border border-cyan-950 rounded-xl p-3 flex justify-between">
                      <span className="font-bold">{c.name}</span>
                      <span className="text-cyan-400">{c.type}</span>
                    </div>
                  ))}
                </div>
              )}

              {tab === 'users' && (
                <div className="space-y-2">
                  {users.map(u => (
                    <div key={u.id} className="bg-[#111a2e] border border-cyan-950 rounded-xl p-3 flex flex-wrap items-center justify-between gap-2">
                      <div>
                        <span className="font-bold">{u.fullName}</span>
                        <span className="text-slate-500 ml-2">{u.email}</span>
                        <span className={`ml-2 text-[10px] ${u.isActive ? 'text-emerald-400' : 'text-rose-400'}`}>{u.isActive ? 'Active' : 'Locked'}</span>
                      </div>
                      <div className="flex gap-1">
                        {['Customer','Staff','Manager','Admin'].map(r => (
                          <button key={r} onClick={() => handleUpdateRole(u.id, r)}
                            className={`px-2 py-1 rounded text-[10px] border ${u.role === r ? 'border-cyan-400 text-cyan-400' : 'border-cyan-950 text-slate-500'}`}>{r}</button>
                        ))}
                        <button onClick={() => handleToggleActive(u.id)} className="px-2 py-1 bg-rose-950 border border-rose-800 rounded text-[10px]">{u.isActive ? 'Khóa' : 'Mở'}</button>
                      </div>
                    </div>
                  ))}
                </div>
              )}

              {tab === 'settings' && (
                <div className="space-y-3">
                  {settings.map((s, i) => (
                    <div key={s.key} className="flex items-center gap-3">
                      <label className="w-32 text-slate-400 font-bold">{s.key}</label>
                      <input value={s.value} onChange={e => { const u = [...settings]; u[i] = {...s, value: e.target.value}; setSettings(u); }}
                        className="flex-1 bg-[#111a2e] border border-cyan-950 rounded px-3 py-1.5" />
                    </div>
                  ))}
                  <button onClick={handleSaveSettings} className="flex items-center gap-2 px-4 py-2 bg-cyan-400 text-slate-950 font-bold rounded-xl"><Save className="w-4 h-4" /> Lưu cấu hình</button>
                </div>
              )}

              {tab === 'backup' && (
                <div className="space-y-4">
                  <button onClick={handleBackup} className="px-4 py-2 bg-cyan-400 text-slate-950 font-bold rounded-xl">Sao lưu dữ liệu ngay</button>
                  {backupData && (
                    <pre className="bg-[#111a2e] border border-cyan-950 rounded-xl p-3 overflow-auto max-h-60 text-[10px] text-slate-400">
                      {JSON.stringify(backupData, null, 2)}
                    </pre>
                  )}
                </div>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
}
