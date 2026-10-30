import { useState, useEffect, useLayoutEffect, useRef, useCallback } from 'react';
import { ArrowLeft, BarChart3, Users, Package, FolderTree, Settings, Database, Loader2, X, Save, Search, Pencil, Eye, EyeOff, Tag, ImagePlus, Plus, Minus, ClipboardList, KeyRound, UserPlus, ShieldCheck, FileClock, Download, RefreshCw, UserRound } from 'lucide-react';
import { adminService, orderService, getApiErrorMessage } from '../services/api';
import OrderDocumentPreview from './OrderDocumentPreview';
import CustomerLookup from './CustomerLookup';
import './AdminPanel.css';

const TABS = [
  { id: 'stats', label: 'Báo cáo', icon: BarChart3 },
  { id: 'orders', label: 'Đơn hàng', icon: Package },
  { id: 'order-requests', label: 'Yêu cầu hủy/hoàn', icon: ClipboardList },
  { id: 'customers', label: 'Tra cứu KH', icon: Search },
  { id: 'products', label: 'Sản phẩm', icon: Package },
  { id: 'inventory', label: 'Quản lý kho', icon: Database },
  { id: 'promotions', label: 'Giá & Voucher', icon: Tag },
  { id: 'categories', label: 'Danh mục', icon: FolderTree },
  { id: 'users', label: 'Nhân viên', icon: Users },
  { id: 'accounts', label: 'Tài khoản', icon: ShieldCheck },
  { id: 'permissions', label: 'Phân quyền', icon: ShieldCheck },
  { id: 'audit-logs', label: 'Nhật ký hoạt động', icon: FileClock },
  { id: 'settings', label: 'Cấu hình', icon: Settings },
  { id: 'backup', label: 'Sao lưu', icon: Database },
];

const CARRIERS = ['GHN', 'GHTK', 'Viettel Post', 'J&T Express', 'VNPost', 'Khác'];
const PROTECTED_ADMIN_EMAIL = 'supportadmin@pcstore.com';
const ORDER_STATUS_LABELS = {
  Pending: 'Chờ xử lý',
  Confirmed: 'Đã xác nhận / Đang đóng gói',
  Shipping: 'Đã bàn giao vận chuyển / Đang giao hàng',
  Completed: 'Đã giao thành công',
  Cancelled: 'Đã hủy',
  DeliveryFailed: 'Giao hàng thất bại',
};

const PRODUCT_SPEC_FIELDS = {
  CPU: [
    { field: 'socket', label: 'Socket', required: true },
    { field: 'tdpWattage', label: 'TDP (W)', type: 'number' },
  ],
  Mainboard: [
    { field: 'socket', label: 'Socket', required: true },
    { field: 'chipset', label: 'Chipset', required: true },
    { field: 'ramType', label: 'Chuẩn RAM', type: 'select', options: ['DDR4', 'DDR5'], required: true },
    { field: 'formFactor', label: 'Form factor', type: 'select', options: ['ATX', 'Micro-ATX', 'Mini-ITX', 'E-ATX'], required: true },
    { field: 'ramSlots', label: 'Số khe RAM', type: 'number' },
    { field: 'ramBusSpeed', label: 'Bus RAM tối đa (MHz)', type: 'number' },
  ],
  RAM: [
    { field: 'ramType', label: 'Chuẩn RAM', type: 'select', options: ['DDR3', 'DDR4', 'DDR5'], required: true },
    { field: 'ramBusSpeed', label: 'Bus RAM (MHz)', type: 'number' },
  ],
  GPU: [
    { field: 'tdpWattage', label: 'Công suất tiêu thụ (W)', type: 'number' },
    { field: 'recommendedPsu', label: 'Nguồn khuyến nghị (W)', type: 'number' },
    { field: 'lengthMm', label: 'Chiều dài GPU (mm)', type: 'number' },
  ],
  SSD: [{ field: 'formFactor', label: 'Chuẩn/kích thước' }],
  HDD: [{ field: 'formFactor', label: 'Kích thước' }],
  PSU: [{ field: 'tdpWattage', label: 'Công suất nguồn (W)', type: 'number', required: true }],
  Case: [
    { field: 'formFactor', label: 'Chuẩn case', type: 'select', options: ['ATX', 'Micro-ATX', 'Mini-ITX', 'E-ATX'] },
    { field: 'lengthMm', label: 'Chiều dài GPU tối đa (mm)', type: 'number' },
  ],
  Cooler: [
    { field: 'socket', label: 'Socket hỗ trợ', required: true },
    { field: 'tdpWattage', label: 'TDP tản nhiệt (W)', type: 'number' },
  ],
};

const emptyProduct = () => ({
  name: '', sku: '', brand: '', description: '', price: 0, promotionalPrice: '', promotionStartsAt: '', promotionEndsAt: '', stockQuantity: 0,
  categoryId: '', imageUrl: '', socket: '', chipset: '', formFactor: '', ramType: '',
  ramBusSpeed: '', ramSlots: '', tdpWattage: '', recommendedPsu: '', lengthMm: '',
  additionalSpecs: {},
});

const emptyVoucher = () => ({
  code: '', discountType: 'Percentage', discountValue: '', minimumOrderAmount: 0,
  maximumDiscountAmount: '', usageLimit: 1, startsAt: '', endsAt: '',
});

const toLocalDateTimeInput = value => {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 16);
};

const toDateInputValue = date => {
  const local = new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
  return local.toISOString().slice(0, 10);
};

const REPORT_PURPOSE_LABELS = {
  gaming: 'Gaming',
  graphics: 'Đồ họa / dựng phim',
  office: 'Văn phòng / học tập',
  streaming: 'Gaming / livestream',
  general: 'Đa dụng',
};

const REPORT_COLORS = ['#22d3ee', '#3b82f6', '#a78bfa', '#34d399', '#fbbf24', '#fb7185'];

const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, character => ({
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
  "'": '&#39;',
}[character]));

const escapeXml = value => String(value ?? '').replace(/[&<>"']/g, character => ({
  '&': '&amp;',
  '<': '&lt;',
  '>': '&gt;',
  '"': '&quot;',
  "'": '&apos;',
}[character]));

const downloadBlob = (blob, fileName) => {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
};

const formatFileSize = bytes => {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
};

const auditRequestFilters = filters => ({
  ...filters,
  fromDate: filters.fromDate ? `${filters.fromDate}T00:00:00+07:00` : undefined,
  toDate: filters.toDate ? `${filters.toDate}T23:59:59.999+07:00` : undefined,
});

const readAuditDetails = details => {
  if (!details) return null;
  try {
    return JSON.parse(details);
  } catch {
    return { description: details };
  }
};

const exportEmployeeCsv = employees => {
  const rows = [
    ['Tên', 'Email', 'Vai trò', 'Số đơn xử lý', 'Đơn thành công', 'Tỷ lệ thành công (%)', 'Thời gian đóng gói TB (phút)', 'Lần cập nhật cuối'],
    ...employees.map(user => [
      user.fullName,
      user.email,
      user.role,
      user.totalHandledOrders ?? 0,
      user.successfulOrders ?? 0,
      user.successRate ?? 0,
      user.avgPackagingMinutes ?? 0,
      user.lastHandledAt ? new Date(user.lastHandledAt).toLocaleString('vi-VN') : 'Chưa xử lý'
    ])
  ];
  const csv = rows.map(row => row.map(value => `"${String(value ?? '').replace(/"/g, '""')}"`).join(',')).join('\n');
  const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
  const fileName = `bao-cao-nhan-vien-${Date.now()}.csv`;
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.click();
  URL.revokeObjectURL(url);
};

function AdminEmptyState({ title, description, icon: Icon = Package }) {
  return (
    <div className="rounded-3xl border border-dashed border-slate-200 bg-white/85 px-4 py-12 text-center shadow-[0_12px_28px_rgba(148,163,184,0.06)] sm:py-14">
      <span className="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-slate-100 text-slate-400">
        <Icon className="h-5 w-5" />
      </span>
      <p className="mt-3 text-sm font-bold text-slate-700">{title}</p>
      <p className="mt-1 text-xs text-slate-400">{description}</p>
    </div>
  );
}

export default function AdminPanel({ onClose, staffName = 'Nhân viên', currentUserId, catalogOnly = false, permissions = [], isAdministrator = false }) {
  const [tab, setTab] = useState(catalogOnly ? 'products' : 'stats');
  const [loading, setLoading] = useState(false);
  const [stats, setStats] = useState(null);
  const [reportRevision, setReportRevision] = useState(0);
  const [reportRenderMs, setReportRenderMs] = useState(null);
  const reportLoadStartedAt = useRef(null);
  const reportRequestId = useRef(0);
  const [users, setUsers] = useState([]);
  const [accounts, setAccounts] = useState([]);
  const [permissionRole, setPermissionRole] = useState('Staff');
  const [permissionMatrix, setPermissionMatrix] = useState(null);
  const [permissionSaving, setPermissionSaving] = useState(false);
  const [auditDraft, setAuditDraft] = useState({ fromDate: '', toDate: '', user: '', action: '', status: '' });
  const [auditFilters, setAuditFilters] = useState({ fromDate: '', toDate: '', user: '', action: '', status: '' });
  const [auditPage, setAuditPage] = useState(1);
  const [auditPageData, setAuditPageData] = useState({ items: [], page: 1, pageSize: 50, totalCount: 0 });
  const [selectedAuditLog, setSelectedAuditLog] = useState(null);
  const [auditExporting, setAuditExporting] = useState(false);
  const [accountSearch, setAccountSearch] = useState('');
  const [accountRoleFilter, setAccountRoleFilter] = useState('ALL');
  const [accountStatusFilter, setAccountStatusFilter] = useState('ALL');
  const [selectedAccountId, setSelectedAccountId] = useState(null);
  const [accountForm, setAccountForm] = useState({ fullName: '', email: '', phoneNumber: '', role: 'Staff' });
  const [accountActionLoading, setAccountActionLoading] = useState(false);
  const [products, setProducts] = useState([]);
  const [inventoryItems, setInventoryItems] = useState([]);
  const [inventoryAction, setInventoryAction] = useState(null);
  const [inventoryForm, setInventoryForm] = useState({
    quantity: '', unitCost: '', supplier: '', note: '', actualStock: '',
  });
  const [inventoryThresholds, setInventoryThresholds] = useState({});
  const [inventorySearch, setInventorySearch] = useState('');
  const [inventoryCategoryFilter, setInventoryCategoryFilter] = useState('ALL');
  const [inventoryStatusFilter, setInventoryStatusFilter] = useState('ALL');
  const [vouchers, setVouchers] = useState([]);
  const [categories, setCategories] = useState([]);
  const [productBrands, setProductBrands] = useState([]);
  const [brands, setBrands] = useState([]);
  const [orders, setOrders] = useState([]);
  const [orderActionRequests, setOrderActionRequests] = useState([]);
  const [orderActionFilter, setOrderActionFilter] = useState('Pending');
  const [orderFilter, setOrderFilter] = useState('ALL');
  const [orderSearch, setOrderSearch] = useState('');
  const [orderReviewNotes, setOrderReviewNotes] = useState({});
  const [reviewingRequestId, setReviewingRequestId] = useState(null);
  const [settings, setSettings] = useState([]);
  const [systemConfiguration, setSystemConfiguration] = useState({
    database: { host: '', port: 5432, database: '', username: '', password: '' },
    ai: { apiKey: '', model: 'gemini-3.8-flash', temperature: 0.7, maxTokens: 1024, ragTopK: 5, ragThreshold: 0.65 },
  });
  const [systemConfigurationStatus, setSystemConfigurationStatus] = useState(null);
  const [configurationHealth, setConfigurationHealth] = useState({ database: null, ai: null });
  const [configurationAction, setConfigurationAction] = useState('');
  const [backups, setBackups] = useState([]);
  const [backupAction, setBackupAction] = useState('');
  const [restoreDialogOpen, setRestoreDialogOpen] = useState(false);
  const [restoreConfirmation, setRestoreConfirmation] = useState('');
  const [restoreTarget, setRestoreTarget] = useState(null);
  const [msg, setMsg] = useState('');
  const [trackingNumbers, setTrackingNumbers] = useState({});
  const [carriers, setCarriers] = useState({});
  const [orderNotes, setOrderNotes] = useState({});
  const [confirmingOrderId, setConfirmingOrderId] = useState(null);
  const [warehouseLocations, setWarehouseLocations] = useState({});
  const [documentOrder, setDocumentOrder] = useState(null);

  const [newProduct, setNewProduct] = useState(emptyProduct);
  const [editingProductId, setEditingProductId] = useState(null);
  const [additionalSpecRows, setAdditionalSpecRows] = useState([]);
  const [productSearch, setProductSearch] = useState('');
  const [productCategoryFilter, setProductCategoryFilter] = useState('ALL');
  const [productBrandFilter, setProductBrandFilter] = useState('ALL');
  const [productStatusFilter, setProductStatusFilter] = useState('ALL');
  const [newCategory, setNewCategory] = useState({ name: '', componentType: 'CPU', description: '', displayOrder: 0 });
  const [editingCategoryId, setEditingCategoryId] = useState(null);
  const [newBrand, setNewBrand] = useState({ name: '', logoUrl: '', description: '', displayOrder: 0 });
  const [editingBrandId, setEditingBrandId] = useState(null);
  const [newVoucher, setNewVoucher] = useState(emptyVoucher);
  const [editingVoucherId, setEditingVoucherId] = useState(null);
  const [statsFromDate, setStatsFromDate] = useState(() => {
    const today = new Date();
    const from = new Date(today);
    from.setDate(today.getDate() - 29);
    return toDateInputValue(from);
  });
  const [statsToDate, setStatsToDate] = useState(() => toDateInputValue(new Date()));
  const [statsPreset, setStatsPreset] = useState('30days');
  const [statsCategoryId, setStatsCategoryId] = useState('ALL');
  const [reportExporting, setReportExporting] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);

  useEffect(() => {
    const updateCurrentTime = () => setCurrentTime(Date.now());
    updateCurrentTime();
    const intervalId = window.setInterval(updateCurrentTime, 60_000);
    return () => window.clearInterval(intervalId);
  }, []);

  useLayoutEffect(() => {
    if (reportLoadStartedAt.current === null) return;
    setReportRenderMs(Math.round(performance.now() - reportLoadStartedAt.current));
    reportLoadStartedAt.current = null;
  }, [reportRevision]);

  const loadTab = useCallback(async (t) => {
    const requestId = t === 'stats' ? ++reportRequestId.current : null;
    if (t !== 'stats') reportRequestId.current += 1;
    if (t === 'stats' && statsFromDate && statsToDate && statsFromDate > statsToDate) {
      reportLoadStartedAt.current = null;
      setLoading(false);
      setMsg('Ngày bắt đầu không được lớn hơn ngày kết thúc.');
      return;
    }
    setLoading(true);
    setMsg('');
    try {
      if (t === 'stats') {
        reportLoadStartedAt.current = performance.now();
        const [statsData, categoryData] = await Promise.all([
          adminService.getStats(statsFromDate, statsToDate, statsCategoryId === 'ALL' ? undefined : Number(statsCategoryId)),
          adminService.getCategories(),
        ]);
        if (requestId !== reportRequestId.current) return;
        setStats(statsData);
        setCategories(categoryData);
        setReportRevision(revision => revision + 1);
      }
      if (t === 'accounts') setAccounts(await adminService.getAccounts());
      if (t === 'permissions') setPermissionMatrix(await adminService.getRolePermissions(permissionRole));
      if (t === 'backup') {
        const backupData = await adminService.getBackups();
        setBackups(backupData.items);
      }
      if (t === 'audit-logs') setAuditPageData(await adminService.getAuditLogs({
        ...auditRequestFilters(auditFilters),
        page: auditPage,
        pageSize: 50,
      }));
      if (t === 'users') setUsers(await adminService.getUsers());
      if (t === 'products') {
        const [productData, categoryData, brandData] = await Promise.all([
          adminService.getProducts(),
          adminService.getCategories(),
          adminService.getBrands()
        ]);
        setProducts(productData);
        setCategories(categoryData);
        setProductBrands(brandData.filter(brand => brand.isActive));
      }
      if (t === 'inventory') {
        const [inventoryData, categoryData] = await Promise.all([
          adminService.getInventory(),
          adminService.getCategories(),
        ]);
        setInventoryItems(inventoryData);
        setCategories(categoryData);
      }
      if (t === 'categories') {
        const [categoryData, brandData] = await Promise.all([adminService.getCategories(), adminService.getBrands()]);
        setCategories(categoryData);
        setBrands(brandData);
      }
      if (t === 'orders') {
        const [orderData, requestData] = await Promise.all([
          orderService.getAllOrders(),
          orderService.getOrderActionRequests(orderActionFilter),
        ]);
        setOrders(orderData);
        setOrderActionRequests(requestData);
      }
      if (t === 'order-requests') setOrderActionRequests(await orderService.getOrderActionRequests(orderActionFilter));
      if (t === 'promotions') {
        const [productData, voucherData, categoryData, brandData] = await Promise.all([
          adminService.getProducts(),
          adminService.getVouchers(),
          adminService.getCategories(),
          adminService.getBrands()
        ]);
        setProducts(productData);
        setVouchers(voucherData);
        setCategories(categoryData);
        setProductBrands(brandData.filter(brand => brand.isActive));
      }
      if (t === 'settings') {
        const [legacySettings, configuration] = await Promise.all([
          adminService.getSettings(),
          adminService.getSystemConfiguration(),
        ]);
        setSettings(legacySettings);
        setSystemConfigurationStatus(configuration);
        setSystemConfiguration({
          database: {
            host: configuration.databaseHost,
            port: configuration.databasePort,
            database: configuration.databaseName,
            username: configuration.databaseUsername,
            password: '',
          },
          ai: {
            apiKey: '',
            model: configuration.aiModel,
            temperature: configuration.aiTemperature,
            maxTokens: configuration.aiMaxTokens,
            ragTopK: configuration.ragTopK,
            ragThreshold: configuration.ragThreshold,
          },
        });
        setConfigurationHealth({ database: null, ai: null });
      }
    } catch (error) {
      if (t === 'stats' && requestId !== reportRequestId.current) return;
      if (t === 'stats') reportLoadStartedAt.current = null;
      setMsg(getApiErrorMessage(error, 'Không thể tải dữ liệu. Hãy làm mới hoặc thử lại.'));
    }
    finally {
      if (t !== 'stats' || requestId === reportRequestId.current) setLoading(false);
    }
  }, [auditFilters, auditPage, orderActionFilter, permissionRole, statsCategoryId, statsFromDate, statsToDate]);

  useEffect(() => { const run = async () => { await loadTab(tab); }; run(); }, [tab, loadTab]);

  useEffect(() => {
    if (tab === 'inventory') {
      let active = true;
      let connection;
      const refreshInventory = async () => {
        if (!active) return;
        try {
          setInventoryItems(await adminService.getInventory());
        } catch {
          if (active) setMsg('Không thể làm mới dữ liệu tồn kho theo thời gian thực.');
        }
      };
      const connect = async () => {
        try {
          const connected = await orderService.connectToNewOrderNotifications(
            undefined,
            refreshInventory,
            refreshInventory,
            refreshInventory,
          );
          if (!active) {
            await connected.stop();
            return;
          }
          connection = connected;
        } catch {
          if (active) setMsg('Không kết nối được cập nhật tồn kho realtime. Hãy làm mới danh sách để kiểm tra thay đổi.');
        }
      };
      void connect();
      return () => {
        active = false;
        void connection?.stop();
      };
    }
    if (tab !== 'orders' && tab !== 'order-requests') return undefined;
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
        }, undefined, undefined, async () => {
          if (!active) return;
          setMsg('Có yêu cầu hủy/hoàn cần được xem xét.');
          try {
            setOrderActionRequests(await orderService.getOrderActionRequests(orderActionFilter));
          } catch {
            setMsg('Có yêu cầu mới nhưng không tải lại được danh sách.');
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
  }, [tab, orderActionFilter]);

  const handleReviewOrderAction = async (actionRequest, approve) => {
    setReviewingRequestId(actionRequest.id);
    try {
      const note = orderReviewNotes[actionRequest.id]?.trim() || null;
      const result = await orderService.reviewOrderActionRequest(actionRequest.id, approve, note);
      setMsg(result.message);
      setOrderActionRequests(await orderService.getOrderActionRequests(orderActionFilter));
      if (tab === 'orders') setOrders(await orderService.getAllOrders(orderFilter === 'ALL' ? undefined : orderFilter));
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể xử lý yêu cầu.'));
    } finally {
      setReviewingRequestId(null);
    }
  };

  const filteredInventory = inventoryItems.filter(item => {
    const search = inventorySearch.trim().toLowerCase();
    const statusMatch = inventoryStatusFilter === 'ALL'
      || (inventoryStatusFilter === 'LowStock' && item.status === 'LowStock')
      || (inventoryStatusFilter === 'OutOfStock' && item.status === 'OutOfStock')
      || (inventoryStatusFilter === 'Available' && item.status === 'Available');
    return (!search || item.name.toLowerCase().includes(search) || item.sku.toLowerCase().includes(search))
      && (inventoryCategoryFilter === 'ALL' || String(item.categoryId) === inventoryCategoryFilter)
      && statusMatch;
  });
  const filteredOrders = orders.filter(order => {
    const search = orderSearch.trim().toLowerCase();
    return (orderFilter === 'ALL' || order.status === orderFilter)
      && (!search
        || order.orderCode.toLowerCase().includes(search)
        || order.receiverName.toLowerCase().includes(search)
        || order.receiverPhone.includes(search));
  });

  const [employeeSearch, setEmployeeSearch] = useState('');
  const [employeeRoleFilter, setEmployeeRoleFilter] = useState('ALL');
  const [selectedEmployeeId, setSelectedEmployeeId] = useState(null);

  const filteredEmployees = (users || []).filter(user => {
    const search = employeeSearch.trim().toLowerCase();
    const matchesSearch = !search
      || user.fullName.toLowerCase().includes(search)
      || user.email.toLowerCase().includes(search)
      || (user.phoneNumber || '').toLowerCase().includes(search);
    const matchesRole = employeeRoleFilter === 'ALL' || user.role === employeeRoleFilter;
    return matchesSearch && matchesRole;
  });

  const selectedEmployee = filteredEmployees.find(user => user.id === selectedEmployeeId)
    || filteredEmployees[0]
    || null;

  const filteredAccounts = (accounts || []).filter(account => {
    const search = accountSearch.trim().toLowerCase();
    const matchesSearch = !search
      || account.fullName.toLowerCase().includes(search)
      || account.email.toLowerCase().includes(search)
      || (account.phoneNumber || '').toLowerCase().includes(search);
    const matchesRole = accountRoleFilter === 'ALL' || account.role === accountRoleFilter;
    const matchesStatus = accountStatusFilter === 'ALL'
      || (accountStatusFilter === 'Active' && account.isActive)
      || (accountStatusFilter === 'Locked' && !account.isActive);
    return matchesSearch && matchesRole && matchesStatus;
  });
  const selectedAccount = filteredAccounts.find(account => account.id === selectedAccountId)
    || filteredAccounts[0]
    || null;

  const handleCreateAccount = async event => {
    event.preventDefault();
    setAccountActionLoading(true);
    try {
      const result = await adminService.createAccount({
        fullName: accountForm.fullName.trim(),
        email: accountForm.email.trim(),
        phoneNumber: accountForm.phoneNumber.trim() || null,
        role: accountForm.role,
      });
      setAccounts(await adminService.getAccounts());
      setSelectedAccountId(result.id);
      setAccountForm({ fullName: '', email: '', phoneNumber: '', role: 'Staff' });
      setMsg(result.message || 'Đã tạo tài khoản và gửi thông tin đăng nhập qua email.');
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể tạo tài khoản.'));
    } finally {
      setAccountActionLoading(false);
    }
  };

  const handleCreateProtectedAdmin = async () => {
    if (accounts.some(account => account.email.toLowerCase() === PROTECTED_ADMIN_EMAIL)) {
      setMsg(`Tài khoản ${PROTECTED_ADMIN_EMAIL} đã tồn tại; không tạo trùng.`);
      return;
    }
    if (!window.confirm(`Tạo tài khoản Admin bảo vệ ${PROTECTED_ADMIN_EMAIL}? Mật khẩu tạm sẽ được gửi qua email.`))
      return;

    setAccountActionLoading(true);
    try {
      const result = await adminService.createAccount({
        fullName: 'PCSTORE Support Admin',
        email: PROTECTED_ADMIN_EMAIL,
        role: 'Admin',
      });
      setAccounts(await adminService.getAccounts());
      setSelectedAccountId(result.id);
      setMsg(result.message || 'Đã tạo Admin bảo vệ và gửi mật khẩu tạm qua email.');
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể tạo tài khoản Admin bảo vệ.'));
    } finally {
      setAccountActionLoading(false);
    }
  };

  const handleResetAccountPassword = async account => {
    if (!window.confirm(`Tạo mật khẩu tạm mới và gửi đến ${account.email}?`)) return;
    setAccountActionLoading(true);
    try {
      const result = await adminService.resetAccountPassword(account.id);
      setMsg(result.message || 'Đã đặt lại mật khẩu và gửi qua email.');
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể đặt lại mật khẩu.'));
    } finally {
      setAccountActionLoading(false);
    }
  };

  const handleAccountRoleChange = async (account, role) => {
    try {
      const result = await adminService.updateUserRole(account.id, role);
      setAccounts(await adminService.getAccounts());
      setMsg(result.message || 'Đã cập nhật vai trò.');
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể cập nhật vai trò.'));
    }
  };

  const handleAccountStatusToggle = async account => {
    try {
      const result = await adminService.toggleUserActive(account.id);
      setAccounts(await adminService.getAccounts());
      setMsg(result.message || 'Đã cập nhật trạng thái tài khoản.');
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể cập nhật trạng thái tài khoản.'));
    }
  };

  const exportEmployeeReport = () => {
    exportEmployeeCsv(filteredEmployees);
  };

  const setRevenuePreset = preset => {
    setStatsPreset(preset);
    if (preset === 'custom') return;
    const end = new Date();
    end.setHours(0, 0, 0, 0);
    const start = new Date(end);
    if (preset === '7days') {
      start.setDate(start.getDate() - 6);
    } else if (preset === 'month') {
      start.setDate(1);
    } else if (preset === 'quarter') {
      start.setMonth(Math.floor(start.getMonth() / 3) * 3, 1);
    } else {
      start.setDate(start.getDate() - 29);
    }
    setStatsFromDate(toDateInputValue(start));
    setStatsToDate(toDateInputValue(end));
  };

  const handleExportRevenueReport = async format => {
    if (!stats) return;
    setReportExporting(true);
    setMsg('');
    const fileName = `bao-cao-doanh-thu-${statsFromDate}-${statsToDate}`;
    try {
      if (format === 'xls') {
        const sheets = [
          ['Tổng quan', [
            ['Chỉ số', 'Giá trị'],
            ['Từ ngày', statsFromDate],
            ['Đến ngày', statsToDate],
            ['Danh mục', categories.find(category => String(category.id) === statsCategoryId)?.name || 'Tất cả'],
            ['Doanh thu', stats.totalRevenue || 0],
            ['Tổng đơn hàng', stats.totalOrders || 0],
            ['Đơn hoàn tất', stats.completedOrders || 0],
            ['Đơn hủy / thất bại', stats.cancelledOrders || 0],
            ['Đơn chờ xử lý', stats.pendingOrders || 0],
            ['Số linh kiện bán ra', stats.totalComponentsSold || 0],
            ['Giá trị trung bình / đơn', stats.averageOrderValue || 0],
            ['Tăng trưởng so với kỳ trước (%)', stats.growthRate || 0],
          ]],
          ['Doanh thu theo ngày', [
            ['Ngày', 'Doanh thu', 'Số đơn'],
            ...(stats.revenueByDay || []).map(item => [item.label, item.revenue, item.orders]),
          ]],
          ['Doanh thu theo danh mục', [
            ['Danh mục', 'Doanh thu', 'Số đơn'],
            ...(stats.revenueByCategory || []).map(item => [item.category, item.revenue, item.orders]),
          ]],
          ['Top 10 linh kiện', [
            ['Xếp hạng', 'Linh kiện', 'Danh mục', 'Số lượng bán', 'Doanh thu'],
            ...(stats.topSellingComponents || []).map((item, index) => [index + 1, item.productName, item.categoryName, item.unitsSold, item.revenue]),
          ]],
          ['Cấu hình PC', [
            ['Mục đích', 'Số cấu hình', 'Tương thích', 'Giá trung bình'],
            ...(stats.pcBuildTrends || []).map(item => [
              REPORT_PURPOSE_LABELS[item.purpose] || item.purpose,
              item.configurations,
              item.compatibleConfigurations,
              item.averageConfigurationPrice,
            ]),
          ]],
          ['Thanh toán', [
            ['Phương thức', 'Doanh thu', 'Số đơn'],
            ...(stats.revenueByPaymentMethod || []).map(item => [item.paymentMethod, item.revenue, item.orders]),
          ]],
        ];
        const worksheetXml = sheets.map(([name, rows]) => `
          <Worksheet ss:Name="${escapeXml(name)}"><Table>
            ${rows.map((row, rowIndex) => `<Row${rowIndex === 0 ? ' ss:StyleID="Header"' : ''}>${row.map(value => {
              const isNumber = typeof value === 'number' && Number.isFinite(value);
              return `<Cell><Data ss:Type="${isNumber ? 'Number' : 'String'}">${escapeXml(value)}</Data></Cell>`;
            }).join('')}</Row>`).join('')}
          </Table></Worksheet>`).join('');
        const excelXml = `<?xml version="1.0" encoding="UTF-8"?>
          <?mso-application progid="Excel.Sheet"?>
          <Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet"
            xmlns:o="urn:schemas-microsoft-com:office:office"
            xmlns:x="urn:schemas-microsoft-com:office:excel"
            xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
            <Styles><Style ss:ID="Header"><Font ss:Bold="1" /></Style></Styles>
            ${worksheetXml}
          </Workbook>`;
        await adminService.recordReportExport({
          format,
          fromDate: statsFromDate,
          toDate: statsToDate,
          categoryId: statsCategoryId === 'ALL' ? null : Number(statsCategoryId),
        });
        downloadBlob(new Blob(['\uFEFF', excelXml], { type: 'application/vnd.ms-excel;charset=utf-8;' }), `${fileName}.xls`);
      } else {
        const html2pdf = (await import('html2pdf.js')).default;
        const rows = [
          ['Ngày', 'Doanh thu', 'Số đơn'],
          ...(stats.revenueByDay || []).map(item => [item.label, `${Number(item.revenue || 0).toLocaleString('vi-VN')}đ`, item.orders]),
        ];
        const categoryRows = (stats.revenueByCategory || []).map(item =>
          `<tr><td>${escapeHtml(item.category)}</td><td>${Number(item.revenue).toLocaleString('vi-VN')}đ</td><td>${item.orders}</td></tr>`
        ).join('');
        const paymentRows = (stats.revenueByPaymentMethod || []).map(item =>
          `<tr><td>${escapeHtml(item.paymentMethod)}</td><td>${Number(item.revenue).toLocaleString('vi-VN')}đ</td><td>${item.orders}</td></tr>`
        ).join('');
        const topRows = (stats.topSellingComponents || []).map((item, index) =>
          `<tr><td>${index + 1}</td><td>${escapeHtml(item.productName)}</td><td>${escapeHtml(item.categoryName)}</td><td>${item.unitsSold}</td><td>${Number(item.revenue).toLocaleString('vi-VN')}đ</td></tr>`
        ).join('');
        const buildRows = (stats.pcBuildTrends || []).map(item =>
          `<tr><td>${escapeHtml(REPORT_PURPOSE_LABELS[item.purpose] || item.purpose)}</td><td>${item.configurations}</td><td>${item.compatibleConfigurations}</td><td>${Number(item.averageConfigurationPrice).toLocaleString('vi-VN')}đ</td></tr>`
        ).join('');
        const dailyRows = rows.slice(1).map(row =>
          `<tr><td>${escapeHtml(row[0])}</td><td>${escapeHtml(row[1])}</td><td>${escapeHtml(row[2])}</td></tr>`
        ).join('');
        const report = document.createElement('div');
        report.style.cssText = 'width:780px;padding:28px;color:#111827;background:#fff;font-family:Arial,sans-serif;font-size:12px;';
        report.innerHTML = `
          <h1 style="font-size:22px;margin:0 0 6px;">BÁO CÁO VÀ THỐNG KÊ DOANH THU</h1>
          <p style="margin:0 0 18px;">Thời gian: ${escapeHtml(statsFromDate)} - ${escapeHtml(statsToDate)} · Danh mục: ${escapeHtml(categories.find(category => String(category.id) === statsCategoryId)?.name || 'Tất cả')}</p>
          <table style="width:100%;border-collapse:collapse;margin-bottom:18px;"><tbody>
            <tr><th style="text-align:left;padding:6px;border:1px solid #cbd5e1;">Doanh thu</th><td style="padding:6px;border:1px solid #cbd5e1;">${Number(stats.totalRevenue || 0).toLocaleString('vi-VN')}đ</td><th style="text-align:left;padding:6px;border:1px solid #cbd5e1;">Đơn hoàn tất</th><td style="padding:6px;border:1px solid #cbd5e1;">${stats.completedOrders || 0}</td></tr>
            <tr><th style="text-align:left;padding:6px;border:1px solid #cbd5e1;">Tổng đơn</th><td style="padding:6px;border:1px solid #cbd5e1;">${stats.totalOrders || 0}</td><th style="text-align:left;padding:6px;border:1px solid #cbd5e1;">Giá trị TB / đơn</th><td style="padding:6px;border:1px solid #cbd5e1;">${Number(stats.averageOrderValue || 0).toLocaleString('vi-VN')}đ</td></tr>
            <tr><th style="text-align:left;padding:6px;border:1px solid #cbd5e1;">Linh kiện bán ra</th><td style="padding:6px;border:1px solid #cbd5e1;">${stats.totalComponentsSold || 0}</td><th style="text-align:left;padding:6px;border:1px solid #cbd5e1;">Hủy / thất bại</th><td style="padding:6px;border:1px solid #cbd5e1;">${stats.cancelledOrders || 0}</td></tr>
            <tr><th style="text-align:left;padding:6px;border:1px solid #cbd5e1;">Đơn chờ xử lý</th><td style="padding:6px;border:1px solid #cbd5e1;">${stats.pendingOrders || 0}</td><th style="text-align:left;padding:6px;border:1px solid #cbd5e1;">Tăng trưởng</th><td style="padding:6px;border:1px solid #cbd5e1;">${Number(stats.growthRate || 0).toFixed(1)}%</td></tr>
          </tbody></table>
          <h2 style="font-size:16px;">Doanh thu theo ngày</h2>
          <table style="width:100%;border-collapse:collapse;margin-bottom:18px;"><thead><tr><th>Ngày</th><th>Doanh thu</th><th>Số đơn</th></tr></thead><tbody>${dailyRows}</tbody></table>
          <h2 style="font-size:16px;">Doanh thu theo danh mục</h2>
          <table style="width:100%;border-collapse:collapse;margin-bottom:18px;"><thead><tr><th>Danh mục</th><th>Doanh thu</th><th>Số đơn</th></tr></thead><tbody>${categoryRows}</tbody></table>
          <h2 style="font-size:16px;">Theo phương thức thanh toán</h2>
          <table style="width:100%;border-collapse:collapse;margin-bottom:18px;"><thead><tr><th>Phương thức</th><th>Doanh thu</th><th>Số đơn</th></tr></thead><tbody>${paymentRows}</tbody></table>
          <h2 style="font-size:16px;">Top 10 linh kiện bán chạy</h2>
          <table style="width:100%;border-collapse:collapse;margin-bottom:18px;"><thead><tr><th>#</th><th>Linh kiện</th><th>Danh mục</th><th>Số lượng</th><th>Doanh thu</th></tr></thead><tbody>${topRows}</tbody></table>
          <h2 style="font-size:16px;">Xu hướng cấu hình PC</h2>
          <table style="width:100%;border-collapse:collapse;"><thead><tr><th>Mục đích</th><th>Số cấu hình</th><th>Tương thích</th><th>Giá trung bình</th></tr></thead><tbody>${buildRows}</tbody></table>
          <style>th,td{border:1px solid #cbd5e1;padding:6px;text-align:left}thead{background:#e2e8f0}tr{page-break-inside:avoid}</style>`;
        document.body.appendChild(report);
        try {
          const pdfBlob = await html2pdf().set({
            margin: 10,
            filename: `${fileName}.pdf`,
            image: { type: 'jpeg', quality: 0.98 },
            html2canvas: { scale: 2, useCORS: true },
            jsPDF: { unit: 'mm', format: 'a4', orientation: 'portrait' },
            pagebreak: { mode: ['css', 'legacy'] },
          }).from(report).outputPdf('blob');
          await adminService.recordReportExport({
            format,
            fromDate: statsFromDate,
            toDate: statsToDate,
            categoryId: statsCategoryId === 'ALL' ? null : Number(statsCategoryId),
          });
          downloadBlob(pdfBlob, `${fileName}.pdf`);
        } finally {
          report.remove();
        }
      }
      setMsg(`Đã xuất báo cáo ${format.toUpperCase()} và ghi nhận vào nhật ký hệ thống.`);
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể xuất báo cáo.'));
    } finally {
      setReportExporting(false);
    }
  };

  const openInventoryAction = (type, item) => {
    setInventoryAction({ type, item });
    setInventoryForm({
      quantity: '',
      unitCost: '',
      supplier: '',
      note: '',
      actualStock: String(item.actualStock),
    });
  };

  const handleSaveInventoryAction = async event => {
    event.preventDefault();
    if (!inventoryAction) return;
    try {
      if (inventoryAction.type === 'receive') {
        await adminService.receiveInventory(inventoryAction.item.id, {
          quantity: Number(inventoryForm.quantity),
          unitCost: inventoryForm.unitCost === '' ? null : Number(inventoryForm.unitCost),
          supplier: inventoryForm.supplier.trim() || null,
          note: inventoryForm.note.trim() || null,
        });
        setMsg(`Đã nhập thêm hàng cho ${inventoryAction.item.sku}.`);
      } else {
        await adminService.adjustInventory(inventoryAction.item.id, {
          actualStock: Number(inventoryForm.actualStock),
          note: inventoryForm.note.trim(),
        });
        setMsg(`Đã điều chỉnh tồn kho ${inventoryAction.item.sku}.`);
      }
      setInventoryAction(null);
      setInventoryItems(await adminService.getInventory());
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể cập nhật tồn kho.'));
    }
  };

  const handleSaveInventoryThreshold = async item => {
    const threshold = Number(inventoryThresholds[item.id] ?? item.lowStockThreshold);
    try {
      await adminService.updateLowStockThreshold(item.id, threshold);
      setInventoryItems(await adminService.getInventory());
      setMsg(`Đã cập nhật ngưỡng cảnh báo ${item.sku}.`);
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể cập nhật ngưỡng cảnh báo tồn kho.'));
    }
  };

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
          : `Đơn hàng đã được xác nhận; tồn kho đã được kiểm tra và đặt trước.${result.emailQueued ? ' Email thông báo đã được xếp hàng gửi.' : ''}`);
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
        setMsg(result.emailWarning
          ? `Đã cập nhật trạng thái. ${result.emailWarning}`
          : `Cập nhật trạng thái thành công.${result.emailQueued ? ' Email thông báo đã được xếp hàng gửi.' : ''}`);
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
    try {
      const payload = {
        ...newProduct,
        price: Number(newProduct.price),
        promotionalPrice: newProduct.promotionalPrice === '' ? null : Number(newProduct.promotionalPrice),
        promotionStartsAt: newProduct.promotionStartsAt ? new Date(newProduct.promotionStartsAt).toISOString() : null,
        promotionEndsAt: newProduct.promotionEndsAt ? new Date(newProduct.promotionEndsAt).toISOString() : null,
        stockQuantity: Number(newProduct.stockQuantity),
        categoryId: Number(newProduct.categoryId),
        ramBusSpeed: newProduct.ramBusSpeed === '' ? null : Number(newProduct.ramBusSpeed),
        ramSlots: newProduct.ramSlots === '' ? null : Number(newProduct.ramSlots),
        tdpWattage: newProduct.tdpWattage === '' ? null : Number(newProduct.tdpWattage),
        recommendedPsu: newProduct.recommendedPsu === '' ? null : Number(newProduct.recommendedPsu),
        lengthMm: newProduct.lengthMm === '' ? null : Number(newProduct.lengthMm),
      };
      if (editingProductId) {
        await adminService.updateProduct(editingProductId, payload);
        setMsg('Cập nhật sản phẩm thành công!');
      } else {
        await adminService.createProduct(payload);
        setMsg('Thêm sản phẩm thành công!');
      }
      setEditingProductId(null);
      setNewProduct(emptyProduct());
      setAdditionalSpecRows([]);
      setProducts(await adminService.getProducts());
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể lưu sản phẩm.'));
    }
  };

  const handleEditProduct = (product) => {
    let additionalSpecs = {};
    try {
      additionalSpecs = product.additionalSpecsJson ? JSON.parse(product.additionalSpecsJson) : {};
    } catch {
      setMsg(`Thông số bổ sung của SKU ${product.sku} không đọc được; các trường khác vẫn có thể chỉnh sửa.`);
    }
    setEditingProductId(product.id);
    setNewProduct({
      ...emptyProduct(),
      name: product.name || '',
      sku: product.sku || '',
      brand: product.brand || '',
      description: product.description || '',
      price: product.price ?? 0,
      promotionalPrice: product.promotionalPrice ?? '',
      promotionStartsAt: toLocalDateTimeInput(product.promotionStartsAt),
      promotionEndsAt: toLocalDateTimeInput(product.promotionEndsAt),
      stockQuantity: product.stockQuantity ?? 0,
      categoryId: product.categoryId ?? '',
      imageUrl: product.imageUrl || '',
      socket: product.socket || '',
      chipset: product.chipset || '',
      formFactor: product.formFactor || '',
      ramType: product.ramType || '',
      ramBusSpeed: product.ramBusSpeed ?? '',
      ramSlots: product.ramSlots ?? '',
      tdpWattage: product.tdpWattage ?? '',
      recommendedPsu: product.recommendedPsu ?? '',
      lengthMm: product.lengthMm ?? '',
      additionalSpecs,
    });
    setAdditionalSpecRows(Object.entries(additionalSpecs).map(([key, value]) => ({ key, value: String(value ?? '') })));
    window.scrollTo({ top: 0, behavior: 'smooth' });
  };

  const handleProductImageChange = (event) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (!file) return;
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
      setMsg('Ảnh chỉ hỗ trợ định dạng JPG, PNG hoặc WEBP.');
      return;
    }
    if (file.size <= 0 || file.size > 5 * 1024 * 1024) {
      setMsg('Kích thước ảnh phải lớn hơn 0 và tối đa 5 MB.');
      return;
    }
    const reader = new FileReader();
    reader.onload = () => {
      if (typeof reader.result === 'string') {
        setNewProduct(current => ({ ...current, imageUrl: reader.result }));
        setMsg('');
      } else {
        setMsg('Không thể đọc tệp ảnh đã chọn.');
      }
    };
    reader.onerror = () => setMsg('Không thể đọc tệp ảnh đã chọn.');
    reader.readAsDataURL(file);
  };

  const handleProductCategoryChange = (categoryId) => {
    setNewProduct(current => ({
      ...emptyProduct(),
      ...current,
      categoryId,
      socket: '',
      chipset: '',
      formFactor: '',
      ramType: '',
      ramBusSpeed: '',
      ramSlots: '',
      tdpWattage: '',
      recommendedPsu: '',
      lengthMm: '',
      additionalSpecs: {},
    }));
    setAdditionalSpecRows([]);
  };

  const handleAdditionalSpecChange = (index, field, value) => {
    const updated = additionalSpecRows.map((row, rowIndex) => rowIndex === index ? { ...row, [field]: value } : row);
    setAdditionalSpecRows(updated);
    setNewProduct(product => ({
      ...product,
      additionalSpecs: Object.fromEntries(updated
        .filter(row => row.key.trim())
        .map(row => [row.key.trim(), row.value])),
    }));
  };

  const handleSaveWarehouseLocation = async (product) => {
    try {
      await adminService.updateWarehouseLocation(product.id, warehouseLocations[product.id] ?? product.warehouseLocation ?? '');
      setMsg(`Đã cập nhật vị trí kho cho SKU ${product.sku}.`);
      setProducts(await adminService.getProducts());
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể cập nhật vị trí kho.'));
    }
  };

  const handleSaveVoucher = async (event) => {
    event.preventDefault();
    try {
      const payload = {
        ...newVoucher,
        code: newVoucher.code.trim().toUpperCase(),
        discountValue: Number(newVoucher.discountValue),
        minimumOrderAmount: Number(newVoucher.minimumOrderAmount),
        maximumDiscountAmount: newVoucher.discountType === 'Percentage' && newVoucher.maximumDiscountAmount !== ''
          ? Number(newVoucher.maximumDiscountAmount)
          : null,
        usageLimit: Number(newVoucher.usageLimit),
        startsAt: new Date(newVoucher.startsAt).toISOString(),
        endsAt: new Date(newVoucher.endsAt).toISOString(),
      };
      if (editingVoucherId) {
        await adminService.updateVoucher(editingVoucherId, payload);
        setMsg('Cập nhật voucher thành công.');
      } else {
        await adminService.createVoucher(payload);
        setMsg('Tạo voucher thành công.');
      }
      setNewVoucher(emptyVoucher());
      setEditingVoucherId(null);
      setVouchers(await adminService.getVouchers());
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể lưu voucher.'));
    }
  };

  const handleEditVoucher = voucher => {
    setEditingVoucherId(voucher.id);
    setNewVoucher({
      code: voucher.code,
      discountType: voucher.discountType,
      discountValue: voucher.discountValue,
      minimumOrderAmount: voucher.minimumOrderAmount,
      maximumDiscountAmount: voucher.maximumDiscountAmount ?? '',
      usageLimit: voucher.usageLimit,
      startsAt: toLocalDateTimeInput(voucher.startsAt),
      endsAt: toLocalDateTimeInput(voucher.endsAt),
    });
  };

  const handleToggleVoucher = async voucher => {
    try {
      await adminService.setVoucherActive(voucher.id, !voucher.isActive);
      setVouchers(await adminService.getVouchers());
      setMsg(voucher.isActive ? 'Đã ngừng voucher.' : 'Đã kích hoạt voucher.');
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể đổi trạng thái voucher.'));
    }
  };

  const handleCreateCategory = async (e) => {
    e.preventDefault();
    try {
      if (editingCategoryId) {
        await adminService.updateCategory(editingCategoryId, newCategory);
        setMsg('Cập nhật danh mục thành công!');
      } else {
        await adminService.createCategory(newCategory);
        setMsg('Thêm danh mục thành công!');
      }
      setEditingCategoryId(null);
      setNewCategory({ name: '', componentType: 'CPU', description: '', displayOrder: 0 });
      setCategories(await adminService.getCategories());
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể lưu danh mục.'));
    }
  };

  const handleToggleCategory = async (category) => {
    try {
      await adminService.setCategoryActive(category.id, !category.isActive);
      setCategories(await adminService.getCategories());
      setMsg(category.isActive ? 'Đã ẩn danh mục khỏi website.' : 'Đã kích hoạt danh mục.');
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể cập nhật trạng thái danh mục.'));
    }
  };

  const handleSaveBrand = async (e) => {
    e.preventDefault();
    try {
      if (editingBrandId) {
        await adminService.updateBrand(editingBrandId, newBrand);
        setMsg('Cập nhật thương hiệu thành công!');
      } else {
        await adminService.createBrand(newBrand);
        setMsg('Thêm thương hiệu thành công!');
      }
      setEditingBrandId(null);
      setNewBrand({ name: '', logoUrl: '', description: '', displayOrder: 0 });
      setBrands(await adminService.getBrands());
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể lưu thương hiệu.'));
    }
  };

  const handleToggleBrand = async (brand) => {
    try {
      await adminService.setBrandActive(brand.id, !brand.isActive);
      setBrands(await adminService.getBrands());
      setMsg(brand.isActive ? 'Đã ẩn thương hiệu khỏi bộ lọc website.' : 'Đã kích hoạt thương hiệu.');
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể cập nhật trạng thái thương hiệu.'));
    }
  };

  const handleSaveSettings = async () => {
    try {
      await adminService.updateSettings(settings.map(s => ({ key: s.key, value: s.value })));
      setMsg('Lưu cấu hình phụ thành công!');
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể lưu cấu hình phụ.'));
    }
  };

  const handleTestSystemDatabase = async () => {
    setConfigurationAction('database');
    try {
      const health = await adminService.testSystemDatabase(systemConfiguration.database);
      setConfigurationHealth(current => ({ ...current, database: health }));
    } catch (error) {
      setConfigurationHealth(current => ({
        ...current,
        database: { status: 'Error', latencyMs: null, message: getApiErrorMessage(error, 'Không thể kiểm tra database.') },
      }));
    } finally {
      setConfigurationAction('');
    }
  };

  const handleTestSystemAi = async () => {
    setConfigurationAction('ai');
    try {
      const health = await adminService.testSystemAi(systemConfiguration.ai);
      setConfigurationHealth(current => ({ ...current, ai: health }));
    } catch (error) {
      setConfigurationHealth(current => ({
        ...current,
        ai: { status: 'Error', latencyMs: null, message: getApiErrorMessage(error, 'Không thể kiểm tra API AI.') },
      }));
    } finally {
      setConfigurationAction('');
    }
  };

  const handleSaveSystemConfiguration = async () => {
    setConfigurationAction('save');
    try {
      const result = await adminService.updateSystemConfiguration(systemConfiguration);
      const configuration = await adminService.getSystemConfiguration();
      setSystemConfigurationStatus(configuration);
      setSystemConfiguration(current => ({
        database: { ...current.database, password: '' },
        ai: { ...current.ai, apiKey: '' },
      }));
      setMsg(result.message);
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể lưu cấu hình hệ thống.'));
    } finally {
      setConfigurationAction('');
    }
  };

  const handleCancelSystemConfiguration = async () => {
    setConfigurationAction('load');
    try {
      const configuration = await adminService.getSystemConfiguration();
      setSystemConfigurationStatus(configuration);
      setSystemConfiguration({
        database: {
          host: configuration.databaseHost,
          port: configuration.databasePort,
          database: configuration.databaseName,
          username: configuration.databaseUsername,
          password: '',
        },
        ai: {
          apiKey: '',
          model: configuration.aiModel,
          temperature: configuration.aiTemperature,
          maxTokens: configuration.aiMaxTokens,
          ragTopK: configuration.ragTopK,
          ragThreshold: configuration.ragThreshold,
        },
      });
      setConfigurationHealth({ database: null, ai: null });
      setMsg('Đã hủy thay đổi chưa lưu.');
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể tải lại cấu hình đã lưu.'));
    } finally {
      setConfigurationAction('');
    }
  };

  const handleBackup = async () => {
    setBackupAction('create');
    try {
      const result = await adminService.createBackup();
      const backupData = await adminService.getBackups();
      setBackups(backupData.items);
      setMsg(result.message);
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể tạo bản sao lưu.'));
    } finally {
      setBackupAction('');
    }
  };

  const handleRestoreBackup = async () => {
    if (!restoreTarget || restoreConfirmation !== 'PHUC HOI') return;

    setBackupAction('restore');
    try {
      const result = await adminService.restoreBackup(restoreTarget.id);
      const backupData = await adminService.getBackups();
      setBackups(backupData.items);
      setMsg(result.message);
      setRestoreDialogOpen(false);
      setRestoreConfirmation('');
      setRestoreTarget(null);
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể phục hồi bản sao lưu.'));
    } finally {
      setBackupAction('');
    }
  };

  const filteredProducts = products.filter(product => {
    const search = productSearch.trim().toLowerCase();
    return (!search || product.name.toLowerCase().includes(search) || product.sku.toLowerCase().includes(search))
      && (productCategoryFilter === 'ALL' || String(product.categoryId) === productCategoryFilter)
      && (productBrandFilter === 'ALL' || product.brand?.toLowerCase() === productBrandFilter.toLowerCase())
      && (productStatusFilter === 'ALL' || String(product.isActive) === productStatusFilter);
  });
  const updateSystemConfigurationField = (section, field, value) => {
    setSystemConfiguration(current => ({
      ...current,
      [section]: { ...current[section], [field]: value },
    }));
  };
  const healthStatusClass = health => health?.status === 'Connected'
    ? 'border-emerald-800 bg-emerald-950/30 text-emerald-200'
    : health
      ? 'border-rose-800 bg-rose-950/30 text-rose-200'
      : 'border-cyan-950 bg-[#111a2e] text-slate-400';

  const activeTab = TABS.find(item => item.id === tab) ?? TABS[0];
  const latestRestorableBackup = backups.find(backup => !backup.isSafetyBackup);

  return (
    <div className="admin-workspace fixed inset-0 z-50 flex items-center justify-center bg-slate-950/45 p-2 backdrop-blur-sm sm:p-4">
      <div className="flex h-[92vh] max-h-[92vh] w-full max-w-[1500px] overflow-hidden rounded-[30px] border border-slate-200 bg-[#edf3ff] shadow-[0_35px_90px_rgba(15,23,42,0.18)]">
        <aside className="flex w-[68px] shrink-0 flex-col border-r border-slate-200 bg-white/80 p-2 sm:w-[240px] sm:p-4">
          <div className="mb-6 flex items-center gap-3">
            <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl bg-gradient-to-br from-blue-700 via-cyan-500 to-sky-300 shadow-lg shadow-blue-500/30">
              <ShieldCheck className="h-5 w-5 text-white" />
            </span>
            <div className="hidden min-w-0 sm:block">
              <p className="text-[9px] font-black uppercase tracking-[0.24em] text-sky-700">PCSTORE · WORKSPACE</p>
              <h2 className="truncate text-lg font-black leading-tight tracking-tight text-slate-900">Trung tâm quản trị</h2>
            </div>
          </div>

          <p className="mb-4 hidden px-2 text-[10px] font-black uppercase tracking-[0.18em] text-slate-500 sm:block">Không gian của bạn</p>

          <nav className="min-h-0 flex-1 space-y-1.5 overflow-y-auto">
            {TABS.filter(item => {
              if (catalogOnly && !['products', 'inventory', 'promotions', 'categories', 'orders', 'order-requests'].includes(item.id)) return false;
              const moduleByTab = {
                stats: 'reports', orders: 'orders', 'order-requests': 'orders', customers: 'customers',
                products: 'catalog', categories: 'catalog', inventory: 'inventory', promotions: 'promotions',
                users: 'employees', accounts: 'accounts', permissions: 'permissions', 'audit-logs': 'auditlogs',
                settings: 'settings', backup: 'backup',
              };
              return isAdministrator || permissions.some(permission => permission.module === moduleByTab[item.id]
                && permission.action === 'view'
                && permission.isAllowed);
            }).map(t => {
              const Icon = t.icon;
              const isActive = tab === t.id;
              return (
                <button
                  key={t.id}
                  type="button"
                  onClick={() => setTab(t.id)}
                  title={t.label}
                  aria-current={isActive ? 'page' : undefined}
                  className={`flex w-full items-center justify-center gap-2 rounded-2xl px-1.5 py-2.5 text-left text-sm font-semibold transition sm:justify-between sm:px-3 ${isActive ? 'bg-gradient-to-r from-blue-600 to-cyan-500 text-white shadow-[0_12px_20px_rgba(59,130,246,0.2)]' : 'text-slate-600 hover:bg-slate-100 hover:text-slate-900'}`}
                >
                  <span className="flex items-center gap-2.5">
                    <span className={`flex h-7 w-7 items-center justify-center rounded-xl ${isActive ? 'bg-white/10 text-white' : 'bg-slate-100 text-slate-500'}`}>
                      <Icon className="h-3.5 w-3.5" />
                    </span>
                    <span className="hidden truncate sm:inline">{t.label}</span>
                  </span>
                  {isActive && <span className="hidden text-[10px] font-black uppercase tracking-[0.16em] text-blue-100 sm:inline">ON</span>}
                </button>
              );
            })}
          </nav>

          <div className="mt-3 border-t border-slate-200 pt-3 text-sm text-slate-600">
            <div className="mb-2 flex items-center justify-center gap-2 rounded-xl bg-slate-100 px-2 py-2.5 sm:justify-start sm:px-3">
              <UserRound className="h-4 w-4 shrink-0 text-blue-600" />
              <span className="hidden truncate text-xs font-semibold text-slate-700 sm:inline">{staffName}</span>
            </div>
            <button type="button" onClick={onClose} className="flex w-full items-center justify-center gap-2 rounded-xl px-2 py-2.5 text-xs font-semibold transition hover:bg-blue-50 hover:text-blue-700 sm:justify-start sm:px-3" aria-label="Về cửa hàng">
              <ArrowLeft className="h-4 w-4 shrink-0" />
              <span className="hidden sm:inline">Về cửa hàng</span>
            </button>
          </div>
        </aside>

        <main className="flex min-w-0 flex-1 flex-col bg-[#edf3ff]">
          <header className="flex items-center justify-between gap-3 border-b border-slate-200 bg-[linear-gradient(135deg,#081827_0%,#10253f_38%,#173d63_100%)] px-3 py-3 text-white sm:px-5 sm:py-4">
            <div className="min-w-0">
              <p className="text-[9px] font-black uppercase tracking-[0.22em] text-cyan-300">PCSTORE · ADMIN WORKSPACE</p>
              <p className="mt-0.5 truncate text-xs font-semibold text-slate-100 sm:text-sm">Xin chào, {staffName}. Chọn khu vực quản trị để bắt đầu.</p>
            </div>
            <div className="flex shrink-0 items-center gap-2 sm:gap-3">
              <span className="hidden items-center gap-2 rounded-xl border border-emerald-400/20 bg-emerald-400/10 px-3 py-2 text-[11px] font-semibold text-emerald-200 sm:inline-flex">
                <span className="h-2 w-2 rounded-full bg-emerald-400" />
                Đang làm việc
              </span>
              <button type="button" onClick={onClose} className="inline-flex items-center gap-2 rounded-xl border border-white/10 bg-white/5 px-2.5 py-2 text-[11px] font-semibold text-slate-100 transition hover:bg-white/10 sm:px-3">
                <ArrowLeft className="h-3.5 w-3.5" />
                <span className="hidden sm:inline">Về cửa hàng</span>
              </button>
            </div>
          </header>

          <div className="flex-1 overflow-y-auto px-3 py-4 text-xs text-slate-700 sm:px-5 sm:py-5">
            <div className="mb-5 flex items-center justify-between gap-3 rounded-[26px] border border-slate-200/80 bg-white/85 p-4 shadow-[0_12px_28px_rgba(148,163,184,0.12)] backdrop-blur-sm">
              <div>
                <p className="text-[10px] font-black uppercase tracking-[0.18em] text-blue-600">Bảng điều khiển</p>
                <h1 className="mt-1 text-[26px] font-black tracking-tight text-slate-900">{activeTab.label}</h1>
                <p className="mt-1 text-sm text-slate-500">Theo dõi và xử lý công việc tại một nơi.</p>
              </div>
              <button type="button" onClick={() => loadTab(tab)} disabled={loading} className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-xs font-bold text-slate-600 shadow-sm transition hover:border-blue-200 hover:bg-blue-50 hover:text-blue-700 disabled:opacity-50">
                <RefreshCw className={`h-3.5 w-3.5 ${loading ? 'animate-spin' : ''}`} />
                Làm mới
              </button>
            </div>

            {msg && <div role="status" className="mb-4 flex items-start justify-between gap-3 rounded-2xl border border-blue-100 bg-blue-50 px-4 py-3 text-xs font-medium text-blue-800 shadow-sm"><span>{msg}</span><button type="button" onClick={() => setMsg('')} aria-label="Đóng thông báo" className="shrink-0 text-blue-500 hover:text-blue-800"><X className="h-4 w-4" /></button></div>}

            {loading ? (
              <div className="py-12 text-center"><Loader2 className="w-6 h-6 animate-spin mx-auto text-cyan-400" /></div>
            ) : (
              <>
                {tab === 'stats' && stats && (
                <div className="space-y-4">
                  <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-slate-200 bg-white/90 p-3 shadow-[0_10px_24px_rgba(148,163,184,0.08)]">
                    <div className="flex flex-wrap items-center gap-3">
                      <label className="text-[10px] text-slate-500">
                        Khoảng thời gian
                        <select value={statsPreset} onChange={event => setRevenuePreset(event.target.value)} className="mt-1 block rounded-xl border border-slate-300 bg-slate-50 px-2 py-1.5 text-slate-700 outline-none focus:border-blue-400">
                          <option value="30days">30 ngày gần nhất</option>
                          <option value="today">Hôm nay</option>
                          <option value="7days">7 ngày qua</option>
                          <option value="month">Tháng này</option>
                          <option value="quarter">Quý này</option>
                          <option value="custom">Tùy chọn</option>
                        </select>
                      </label>
                      <label className="text-[10px] text-slate-500">
                        Từ
                        <input type="date" value={statsFromDate} onChange={event => { setStatsPreset('custom'); setStatsFromDate(event.target.value); }} className="mt-1 block rounded-xl border border-slate-300 bg-slate-50 px-2 py-1.5 text-slate-700 outline-none focus:border-blue-400" />
                      </label>
                      <label className="text-[10px] text-slate-500">
                        Đến
                        <input type="date" value={statsToDate} onChange={event => { setStatsPreset('custom'); setStatsToDate(event.target.value); }} className="mt-1 block rounded-xl border border-slate-300 bg-slate-50 px-2 py-1.5 text-slate-700 outline-none focus:border-blue-400" />
                      </label>
                      <label className="text-[10px] text-slate-500">
                        Danh mục linh kiện
                        <select value={statsCategoryId} onChange={event => setStatsCategoryId(event.target.value)} className="mt-1 block min-w-40 rounded-xl border border-slate-300 bg-slate-50 px-2 py-1.5 text-slate-700 outline-none focus:border-blue-400">
                          <option value="ALL">Tất cả danh mục</option>
                          {categories.map(category => <option key={category.id} value={category.id}>{category.name}</option>)}
                        </select>
                      </label>
                      <button type="button" onClick={() => loadTab('stats')} className="rounded-xl bg-gradient-to-r from-blue-600 to-cyan-500 px-3 py-1.5 text-[10px] font-bold text-white shadow-[0_8px_18px_rgba(59,130,246,0.25)] hover:opacity-95">
                        Áp dụng
                      </button>
                    </div>
                    <div className="flex gap-2">
                      {reportRenderMs !== null && (
                        <span role="status" className={`self-center text-[9px] ${reportRenderMs < 2000 ? 'text-emerald-600' : 'text-amber-600'}`}>
                          Báo cáo tải + render {reportRenderMs} ms
                        </span>
                      )}
                      <button type="button" onClick={() => handleExportRevenueReport('xls')} disabled={reportExporting} className="rounded-xl border border-emerald-200 bg-emerald-50 px-3 py-1.5 text-[10px] font-bold text-emerald-700 hover:bg-emerald-100 disabled:opacity-50">
                        {reportExporting ? 'Đang xuất...' : 'Xuất Excel'}
                      </button>
                      <button type="button" onClick={() => handleExportRevenueReport('pdf')} disabled={reportExporting} className="rounded-xl border border-rose-200 bg-rose-50 px-3 py-1.5 text-[10px] font-bold text-rose-700 hover:bg-rose-100 disabled:opacity-50">
                        {reportExporting ? 'Đang xuất...' : 'Xuất PDF'}
                      </button>
                    </div>
                  </div>

                  <div className="grid grid-cols-2 gap-3 sm:grid-cols-3 xl:grid-cols-6">
                    {[
                      { label: 'Doanh thu', value: `${Number(stats.totalRevenue || 0).toLocaleString('vi-VN')}đ`, color: 'text-blue-600' },
                      { label: 'Đơn hoàn tất', value: stats.completedOrders || 0, color: 'text-slate-800' },
                      { label: 'Tổng đơn', value: stats.totalOrders || 0, color: 'text-slate-800' },
                      { label: 'Linh kiện bán ra', value: stats.totalComponentsSold || 0, color: 'text-cyan-700' },
                      { label: 'Hủy / thất bại', value: stats.cancelledOrders || 0, color: 'text-rose-600' },
                      { label: 'TB / đơn', value: `${Number(stats.averageOrderValue || 0).toLocaleString('vi-VN')}đ`, color: 'text-emerald-600' },
                    ].map(s => (
                      <div key={s.label} className="rounded-2xl border border-slate-200 bg-white/90 p-4 shadow-[0_10px_22px_rgba(148,163,184,0.06)]">
                        <p className="text-[10px] font-black uppercase tracking-[0.12em] text-slate-500">{s.label}</p>
                        <p className={`mt-2 text-lg font-black ${s.color}`}>{s.value}</p>
                      </div>
                    ))}
                  </div>

                  <div className="grid gap-4 lg:grid-cols-[1.7fr_1fr]">
                    <div className="rounded-2xl border border-slate-200 bg-white/90 p-4 shadow-[0_10px_24px_rgba(148,163,184,0.08)]">
                      <div className="mb-3 flex items-center justify-between">
                        <h3 className="font-bold text-blue-700">Xu hướng doanh thu</h3>
                        <span className={`text-[10px] font-bold ${Number(stats.growthRate || 0) >= 0 ? 'text-emerald-600' : 'text-rose-600'}`}>
                          {Number(stats.growthRate || 0) >= 0 ? '+' : ''}{Number(stats.growthRate || 0).toFixed(1)}% so với kỳ trước
                        </span>
                      </div>
                      <div className="flex h-52 items-end gap-2 overflow-x-auto pb-2">
                        {(stats.revenueByDay || []).map(item => {
                          const maxValue = Math.max(...(stats.revenueByDay || []).map(day => day.revenue || 0), 1);
                          const height = Math.max(12, ((item.revenue || 0) / maxValue) * 100);
                          return (
                            <div key={`${item.date}-${item.label}`} className="flex min-w-[32px] flex-1 flex-col items-center gap-2">
                              <div className="flex h-full w-full items-end justify-center">
                                <div className="w-full rounded-t-md bg-gradient-to-t from-cyan-500 to-cyan-300" style={{ height: `${height}%` }} title={`${item.label}: ${Number(item.revenue || 0).toLocaleString('vi-VN')}đ`} />
                              </div>
                              <span className="text-[9px] text-slate-400">{item.label}</span>
                            </div>
                          );
                        })}
                      </div>
                      {(() => {
                        const points = stats.revenueByDay || [];
                        if (points.length === 0) return <p className="text-center text-[10px] text-slate-500">Chưa có dữ liệu để vẽ biểu đồ đường.</p>;
                        const maxValue = Math.max(...points.map(point => Number(point.revenue || 0)), 1);
                        const plotted = points.map((point, index) => {
                          const x = points.length === 1 ? 400 : index / (points.length - 1) * 800;
                          const y = 166 - Number(point.revenue || 0) / maxValue * 145;
                          return `${x},${y}`;
                        }).join(' ');
                        return (
                          <div className="mt-3 border-t border-cyan-950 pt-3">
                            <p className="mb-2 text-[9px] text-slate-500">Biểu đồ đường · doanh thu theo ngày</p>
                            <svg viewBox="0 0 800 180" preserveAspectRatio="none" role="img" aria-label="Biểu đồ đường doanh thu theo ngày" className="h-24 w-full overflow-visible">
                              <polyline points={plotted} fill="none" stroke="#a78bfa" strokeWidth="3" strokeLinejoin="round" strokeLinecap="round" vectorEffect="non-scaling-stroke" />
                              {points.map((point, index) => {
                                const x = points.length === 1 ? 400 : index / (points.length - 1) * 800;
                                const y = 166 - Number(point.revenue || 0) / maxValue * 145;
                                return <circle key={point.date} cx={x} cy={y} r="4" fill="#a78bfa"><title>{point.label}: {Number(point.revenue || 0).toLocaleString('vi-VN')}đ</title></circle>;
                              })}
                            </svg>
                          </div>
                        );
                      })()}
                    </div>

                    <div className="rounded-xl border border-cyan-950 bg-[#111a2e] p-4">
                      <h3 className="mb-3 font-bold text-cyan-300">Doanh thu theo danh mục</h3>
                      <div className="space-y-3">
                        {(stats.revenueByCategory || []).map(item => {
                          const percent = Math.max(12, ((item.revenue || 0) / Math.max(...(stats.revenueByCategory || []).map(c => c.revenue || 0), 1)) * 100);
                          return (
                            <div key={item.category}>
                              <div className="mb-1 flex items-center justify-between text-[10px] text-slate-300">
                                <span>{item.category}</span>
                                <span>{Number(item.revenue || 0).toLocaleString('vi-VN')}đ</span>
                              </div>
                              <div className="h-2 rounded-full bg-slate-800">
                                <div className="h-2 rounded-full bg-gradient-to-r from-cyan-400 to-blue-500" style={{ width: `${percent}%` }} />
                              </div>
                            </div>
                          );
                        })}
                        {(!stats.revenueByCategory || stats.revenueByCategory.length === 0) && (
                          <p className="text-[10px] text-slate-400">Chưa có dữ liệu doanh thu theo danh mục trong khoảng thời gian này.</p>
                        )}
                      </div>
                    </div>
                  </div>

                  <div className="grid gap-4 md:grid-cols-2">
                    <div className="rounded-xl border border-cyan-950 bg-[#111a2e] p-4">
                      <h3 className="mb-3 font-bold text-cyan-300">Cơ cấu doanh thu theo thanh toán</h3>
                      {(() => {
                        const payments = stats.revenueByPaymentMethod || [];
                        const paymentTotal = payments.reduce((sum, item) => sum + Number(item.revenue || 0), 0);
                        let position = 0;
                        const gradient = payments.map((item, index) => {
                          const start = position;
                          position += paymentTotal ? Number(item.revenue || 0) / paymentTotal * 100 : 0;
                          return `${REPORT_COLORS[index % REPORT_COLORS.length]} ${start}% ${position}%`;
                        }).join(', ');
                        return payments.length > 0 ? (
                          <div className="flex flex-wrap items-center gap-4">
                            <div aria-label="Biểu đồ tròn cơ cấu doanh thu theo phương thức thanh toán" className="h-32 w-32 shrink-0 rounded-full" style={{ background: `conic-gradient(${gradient})` }} />
                            <div className="min-w-40 flex-1 space-y-2">
                              {payments.map((item, index) => (
                                <div key={item.paymentMethod} className="flex items-center justify-between gap-2 text-[10px] text-slate-300">
                                  <span className="flex items-center gap-2"><i className="h-2.5 w-2.5 rounded-full" style={{ backgroundColor: REPORT_COLORS[index % REPORT_COLORS.length] }} />{item.paymentMethod || 'Chưa xác định'}</span>
                                  <span>{(paymentTotal ? Number(item.revenue || 0) / paymentTotal * 100 : 0).toFixed(1)}% · {Number(item.revenue || 0).toLocaleString('vi-VN')}đ</span>
                                </div>
                              ))}
                            </div>
                          </div>
                        ) : <p className="text-[10px] text-slate-400">Chưa có dữ liệu thanh toán.</p>;
                      })()}
                    </div>

                    <div className="rounded-xl border border-cyan-950 bg-[#111a2e] p-4">
                      <h3 className="mb-3 font-bold text-cyan-300">Tổng quan</h3>
                      <div className="space-y-2 text-[10px] text-slate-300">
                        <div className="flex items-center justify-between rounded-lg border border-cyan-900 bg-[#0c1424] px-3 py-2">
                          <span>Doanh thu ngày gần nhất</span>
                          <strong className="text-cyan-300">{(stats.revenueByDay?.at(-1)?.revenue || 0).toLocaleString('vi-VN')}đ</strong>
                        </div>
                        <div className="flex items-center justify-between rounded-lg border border-cyan-900 bg-[#0c1424] px-3 py-2">
                          <span>Đơn chờ xử lý</span>
                          <strong className="text-amber-300">{stats.pendingOrders || 0}</strong>
                        </div>
                        <div className="flex items-center justify-between rounded-lg border border-cyan-900 bg-[#0c1424] px-3 py-2">
                          <span>Đơn hủy / thất bại</span>
                          <strong className="text-rose-300">{stats.cancelledOrders || 0}</strong>
                        </div>
                        <div className="flex items-center justify-between rounded-lg border border-cyan-900 bg-[#0c1424] px-3 py-2">
                          <span>Khoảng thời gian</span>
                          <strong className="text-white">{new Date(stats.periodStart).toLocaleDateString('vi-VN')} - {new Date(stats.periodEnd).toLocaleDateString('vi-VN')}</strong>
                        </div>
                      </div>
                    </div>
                  </div>

                  <div className="grid gap-4 lg:grid-cols-2">
                    <div className="overflow-hidden rounded-xl border border-cyan-950 bg-[#111a2e]">
                      <h3 className="border-b border-cyan-950 p-4 font-bold text-cyan-300">Top 10 linh kiện bán chạy nhất</h3>
                      {(stats.topSellingComponents || []).length > 0 ? (
                        <div className="overflow-x-auto">
                          <table className="w-full text-left text-[10px]">
                            <thead className="bg-[#0c1424] text-slate-400"><tr><th className="px-3 py-2">#</th><th className="px-3 py-2">Linh kiện</th><th className="px-3 py-2">Danh mục</th><th className="px-3 py-2">SL</th><th className="px-3 py-2">Doanh thu</th></tr></thead>
                            <tbody>{stats.topSellingComponents.map((item, index) => (
                              <tr key={item.productId} className="border-t border-cyan-950 text-slate-300">
                                <td className="px-3 py-2 font-bold text-cyan-300">{index + 1}</td>
                                <td className="px-3 py-2">{item.productName}</td>
                                <td className="px-3 py-2">{item.categoryName}</td>
                                <td className="px-3 py-2">{item.unitsSold}</td>
                                <td className="px-3 py-2">{Number(item.revenue || 0).toLocaleString('vi-VN')}đ</td>
                              </tr>
                            ))}</tbody>
                          </table>
                        </div>
                      ) : <p className="p-4 text-[10px] text-slate-400">Chưa có dữ liệu bán hàng trong kỳ.</p>}
                    </div>

                    <div className="rounded-xl border border-cyan-950 bg-[#111a2e] p-4">
                      <h3 className="mb-4 font-bold text-cyan-300">Xu hướng cấu hình PC được lưu</h3>
                      {(stats.pcBuildTrends || []).length > 0 ? (
                        <div className="space-y-3">
                          {stats.pcBuildTrends.map((item, index) => {
                            const maxBuilds = Math.max(...stats.pcBuildTrends.map(build => build.configurations), 1);
                            const barWidth = Math.max(5, Number(item.configurations || 0) / maxBuilds * 100);
                            return (
                              <div key={item.purpose}>
                                <div className="mb-1 flex items-center justify-between gap-2 text-[10px] text-slate-300">
                                  <span>{REPORT_PURPOSE_LABELS[item.purpose] || item.purpose}</span>
                                  <span>{item.configurations} cấu hình · {Number(item.averageConfigurationPrice || 0).toLocaleString('vi-VN')}đ TB</span>
                                </div>
                                <div className="h-2 rounded-full bg-slate-800">
                                  <div className="h-2 rounded-full" style={{ width: `${barWidth}%`, backgroundColor: REPORT_COLORS[index % REPORT_COLORS.length] }} />
                                </div>
                                <p className="mt-1 text-[9px] text-slate-500">Tương thích: {item.compatibleConfigurations}/{item.configurations}</p>
                              </div>
                            );
                          })}
                        </div>
                      ) : <p className="text-[10px] text-slate-400">Chưa có cấu hình PC được lưu trong kỳ này.</p>}
                    </div>
                  </div>
                </div>
              )}

              {tab === 'order-requests' && (
                <section className="space-y-3">
                  <div className="flex flex-wrap items-center justify-between gap-3">
                    <div>
                      <h3 className="font-bold text-cyan-300">Yêu cầu hủy đơn / hoàn trả linh kiện</h3>
                      <p className="mt-1 text-[11px] text-slate-400">Chỉ Manager/Admin có thể phê duyệt. Chấp thuận sẽ cập nhật trạng thái, hoàn kho phù hợp và gửi thông báo khách hàng.</p>
                    </div>
                    <label className="flex items-center gap-2 text-[10px] text-slate-400">
                      Lọc trạng thái
                      <select value={orderActionFilter} onChange={event => setOrderActionFilter(event.target.value)} className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5 text-slate-200">
                        <option value="Pending">Chờ duyệt</option>
                        <option value="Approved">Đã duyệt</option>
                        <option value="Rejected">Đã từ chối</option>
                        <option value="ALL">Tất cả</option>
                      </select>
                    </label>
                  </div>
                  {orderActionRequests.length === 0 ? (
                    <AdminEmptyState icon={ClipboardList} title="Không có yêu cầu cần hiển thị." description="Các yêu cầu hủy hoặc hoàn trả sẽ xuất hiện tại đây." />
                  ) : orderActionRequests.map(actionRequest => (
                    <article key={actionRequest.id} className="space-y-3 rounded-xl border border-cyan-950 bg-[#111a2e] p-4">
                      <div className="flex flex-wrap items-start justify-between gap-3">
                        <div>
                          <p className="font-bold text-cyan-300">
                            {actionRequest.requestType === 'Cancel' ? 'Yêu cầu hủy đơn' : 'Yêu cầu hoàn trả linh kiện'}
                            {' · '}{actionRequest.orderCode}
                          </p>
                          <p className="mt-1 text-[10px] text-slate-400">
                            {actionRequest.customerName} · {actionRequest.customerEmail || 'Khách vãng lai'}
                            {' · '}{new Date(actionRequest.requestedAt).toLocaleString('vi-VN')}
                          </p>
                          <p className="mt-1 text-[10px] text-slate-400">
                            Trạng thái đơn: {ORDER_STATUS_LABELS[actionRequest.orderStatus] || actionRequest.orderStatus}
                            {' · '}Tổng: {Number(actionRequest.totalAmount).toLocaleString('vi-VN')} đ
                          </p>
                        </div>
                        <span className={`rounded px-2 py-1 text-[10px] font-bold ${actionRequest.status === 'Pending' ? 'bg-amber-950 text-amber-300' : actionRequest.status === 'Approved' ? 'bg-emerald-950 text-emerald-300' : 'bg-rose-950 text-rose-300'}`}>
                          {actionRequest.status === 'Pending' ? 'Chờ duyệt' : actionRequest.status === 'Approved' ? 'Đã duyệt' : 'Đã từ chối'}
                        </span>
                      </div>
                      <p className="rounded-lg bg-[#0c1424] p-3 text-xs text-slate-200">Lý do khách hàng: {actionRequest.reason}</p>
                      {actionRequest.reviewNote && <p className="text-[10px] text-slate-400">Ghi chú xử lý: {actionRequest.reviewNote}</p>}
                      {actionRequest.status === 'Pending' && (
                        <>
                          <label className="block space-y-1 text-[10px] text-slate-400" htmlFor={`order-request-note-${actionRequest.id}`}>
                            Ghi chú gửi khách hàng (không bắt buộc)
                            <textarea id={`order-request-note-${actionRequest.id}`} rows={2} maxLength={500} value={orderReviewNotes[actionRequest.id] || ''} onChange={event => setOrderReviewNotes(current => ({ ...current, [actionRequest.id]: event.target.value }))} className="w-full rounded border border-cyan-950 bg-[#0c1424] px-3 py-2 text-xs text-white" />
                          </label>
                          <div className="flex flex-wrap gap-2">
                            <button type="button" disabled={reviewingRequestId === actionRequest.id} onClick={() => handleReviewOrderAction(actionRequest, true)} className="rounded bg-emerald-800 px-3 py-2 text-[10px] font-bold text-white hover:bg-emerald-700 disabled:opacity-50">
                              {reviewingRequestId === actionRequest.id ? 'Đang xử lý...' : 'Phê duyệt'}
                            </button>
                            <button type="button" disabled={reviewingRequestId === actionRequest.id} onClick={() => handleReviewOrderAction(actionRequest, false)} className="rounded bg-rose-950 px-3 py-2 text-[10px] font-bold text-rose-200 hover:bg-rose-900 disabled:opacity-50">Từ chối</button>
                          </div>
                        </>
                      )}
                    </article>
                  ))}
                </section>
              )}

              {tab === 'orders' && (
                <div className="space-y-2">
                  <div className="grid gap-2 rounded-2xl border border-slate-200 bg-white p-3 shadow-sm sm:grid-cols-2">
                    <label className="sr-only" htmlFor="orders-search">Tìm theo mã đơn, khách hàng hoặc số điện thoại</label>
                    <input id="orders-search" value={orderSearch} onChange={event => setOrderSearch(event.target.value)} placeholder="Tìm mã đơn / khách / số điện thoại" className="rounded border border-cyan-950 bg-[#111a2e] px-2 py-1.5" />
                    <label className="sr-only" htmlFor="orders-status-filter">Lọc trạng thái đơn hàng</label>
                    <select id="orders-status-filter" value={orderFilter} onChange={event => setOrderFilter(event.target.value)} className="rounded border border-cyan-950 bg-[#111a2e] px-2 py-1.5">
                      <option value="ALL">Tất cả trạng thái</option>
                      {Object.entries(ORDER_STATUS_LABELS).map(([status, label]) => <option key={status} value={status}>{label}</option>)}
                    </select>
                  </div>
                  <button type="button" onClick={() => setTab('order-requests')} className="flex w-full items-center justify-between rounded-xl border border-amber-900 bg-amber-950/30 p-3 text-left text-amber-200">
                    <span className="flex items-center gap-2"><ClipboardList className="h-4 w-4" /> Yêu cầu hủy/hoàn chờ duyệt</span>
                    <strong>{orderActionRequests.filter(request => request.status === 'Pending').length}</strong>
                  </button>
                  {filteredOrders.length === 0 ? (
                    <AdminEmptyState
                      title={orders.length === 0 ? 'Chưa có đơn hàng.' : 'Không tìm thấy đơn phù hợp.'}
                      description={orders.length === 0 ? 'Đơn hàng mới sẽ xuất hiện tại đây khi được tạo.' : 'Thử thay đổi từ khóa tìm kiếm hoặc bộ lọc trạng thái.'}
                    />
                  ) : filteredOrders.map(o => (
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
                              ? ['Shipping']
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
                          {(o.status === 'Confirmed' || o.status === 'Shipping') && (
                            <>
                              <button onClick={() => setDocumentOrder({ orderId: o.id, documentType: 'packing-slip' })} className="px-2 py-1 bg-indigo-950 border border-indigo-800 rounded text-[10px]">In phiếu đóng gói</button>
                              <button onClick={() => setDocumentOrder({ orderId: o.id, documentType: 'invoice' })} className="px-2 py-1 bg-indigo-950 border border-indigo-800 rounded text-[10px]">In hóa đơn</button>
                            </>
                          )}
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

              {tab === 'customers' && <CustomerLookup />}

              {tab === 'products' && (
                <div className="space-y-4">
                  <form onSubmit={handleCreateProduct} className="space-y-4 rounded-xl border border-cyan-950 bg-[#111a2e] p-4">
                    <h3 className="font-bold text-cyan-300">{editingProductId ? 'Cập nhật sản phẩm' : 'Thêm sản phẩm mới'}</h3>
                    <div className="grid gap-2 md:grid-cols-2">
                      <input placeholder="Tên sản phẩm *" maxLength={250} value={newProduct.name} onChange={event => setNewProduct({ ...newProduct, name: event.target.value })} className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" required />
                      <input placeholder="SKU *" maxLength={100} value={newProduct.sku} onChange={event => setNewProduct({ ...newProduct, sku: event.target.value })} className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" required />
                      <select value={newProduct.categoryId} onChange={event => handleProductCategoryChange(event.target.value)} className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" required>
                        <option value="">Chọn danh mục *</option>
                        {categories.filter(category => category.isActive || String(category.id) === String(newProduct.categoryId)).map(category => (
                          <option key={category.id} value={category.id} disabled={!category.isActive}>{category.name}{category.isActive ? '' : ' (đang ẩn)'}</option>
                        ))}
                      </select>
                      <select value={newProduct.brand} onChange={event => setNewProduct({ ...newProduct, brand: event.target.value })} className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5">
                        <option value="">Không chọn thương hiệu</option>
                        {productBrands.map(brand => <option key={brand.id} value={brand.name}>{brand.name}</option>)}
                        {newProduct.brand && !productBrands.some(brand => brand.name.toLowerCase() === newProduct.brand.toLowerCase()) && (
                          <option value={newProduct.brand}>{newProduct.brand} (thương hiệu hiện tại)</option>
                        )}
                      </select>
                      <input type="number" min="1" step="1" placeholder="Giá bán (VND) *" value={newProduct.price} onChange={event => setNewProduct({ ...newProduct, price: event.target.value })} className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" required />
                      <input type="number" min="0" step="1" placeholder="Số lượng tồn kho *" value={newProduct.stockQuantity} onChange={event => setNewProduct({ ...newProduct, stockQuantity: event.target.value })} className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" required />
                      <textarea placeholder="Mô tả ngắn" maxLength={2000} value={newProduct.description} onChange={event => setNewProduct({ ...newProduct, description: event.target.value })} className="min-h-20 rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5 md:col-span-2" />
                    </div>

                    <section className="space-y-2 rounded border border-cyan-950 p-3">
                      <h4 className="font-bold text-cyan-200">Giá khuyến mãi theo thời gian (không bắt buộc)</h4>
                      <div className="grid gap-2 md:grid-cols-3">
                        <label className="space-y-1 text-[11px] text-slate-300">
                          <span>Giá khuyến mãi (VND)</span>
                          <input type="number" min="1" max={newProduct.price || undefined} step="1" value={newProduct.promotionalPrice} onChange={event => setNewProduct({ ...newProduct, promotionalPrice: event.target.value })} className="w-full rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" />
                        </label>
                        <label className="space-y-1 text-[11px] text-slate-300">
                          <span>Bắt đầu</span>
                          <input type="datetime-local" required={newProduct.promotionalPrice !== ''} value={newProduct.promotionStartsAt} onChange={event => setNewProduct({ ...newProduct, promotionStartsAt: event.target.value })} className="w-full rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" />
                        </label>
                        <label className="space-y-1 text-[11px] text-slate-300">
                          <span>Kết thúc</span>
                          <input type="datetime-local" required={newProduct.promotionalPrice !== ''} value={newProduct.promotionEndsAt} onChange={event => setNewProduct({ ...newProduct, promotionEndsAt: event.target.value })} className="w-full rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" />
                        </label>
                      </div>
                      <p className="text-[10px] text-slate-500">Đến đúng thời điểm kết thúc, hệ thống tự quay về giá niêm yết. Giá khuyến mãi không vượt giá niêm yết.</p>
                    </section>

                    <div className="flex flex-wrap items-center gap-3 rounded border border-cyan-950 bg-[#0c1424] p-3">
                      <label className="flex cursor-pointer items-center gap-2 rounded bg-cyan-950 px-3 py-2 text-cyan-200">
                        <ImagePlus className="h-4 w-4" /> Chọn ảnh sản phẩm
                        <input type="file" accept="image/jpeg,image/png,image/webp" onChange={handleProductImageChange} className="sr-only" />
                      </label>
                      <span className="text-[11px] text-slate-400">JPG, PNG hoặc WEBP · tối đa 5 MB</span>
                      {newProduct.imageUrl && (
                        <>
                          <img src={newProduct.imageUrl} alt="Xem trước sản phẩm" className="h-14 w-14 rounded border border-cyan-900 object-contain" />
                          <button type="button" onClick={() => setNewProduct({ ...newProduct, imageUrl: '' })} className="rounded border border-rose-900 px-2 py-1 text-rose-300">Bỏ ảnh</button>
                        </>
                      )}
                    </div>

                    {newProduct.categoryId && (
                      <section className="space-y-2 rounded border border-cyan-950 p-3">
                        <h4 className="font-bold text-cyan-200">Thông số kỹ thuật — {categories.find(category => String(category.id) === String(newProduct.categoryId))?.name}</h4>
                        <div className="grid gap-2 md:grid-cols-2">
                          {(PRODUCT_SPEC_FIELDS[categories.find(category => String(category.id) === String(newProduct.categoryId))?.type] || []).map(spec => (
                            <label key={spec.field} className="space-y-1 text-[11px] text-slate-300">
                              <span>{spec.label}{spec.required ? ' *' : ''}</span>
                              {spec.type === 'select' ? (
                                <select required={spec.required} value={newProduct[spec.field]} onChange={event => setNewProduct({ ...newProduct, [spec.field]: event.target.value })} className="w-full rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5">
                                  <option value="">Chọn...</option>
                                  {spec.options.map(option => <option key={option} value={option}>{option}</option>)}
                                </select>
                              ) : (
                                <input type={spec.type || 'text'} min={spec.type === 'number' ? '0' : undefined} step={spec.type === 'number' ? '1' : undefined} required={spec.required} value={newProduct[spec.field]} onChange={event => setNewProduct({ ...newProduct, [spec.field]: event.target.value })} className="w-full rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" />
                              )}
                            </label>
                          ))}
                        </div>
                        <div className="space-y-2">
                          <div className="flex items-center justify-between">
                            <span className="text-[11px] font-bold text-slate-300">Thông số bổ sung</span>
                            <button type="button" onClick={() => setAdditionalSpecRows(current => [...current, { key: '', value: '' }])} className="flex items-center gap-1 text-[11px] text-cyan-300"><Plus className="h-3.5 w-3.5" />Thêm trường</button>
                          </div>
                          {additionalSpecRows.map((row, index) => (
                            <div key={`spec-${index}`} className="grid grid-cols-[1fr_1fr_auto] gap-2">
                              <input value={row.key} maxLength={80} onChange={event => handleAdditionalSpecChange(index, 'key', event.target.value)} placeholder="Tên thông số (vd: Số nhân)" className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" />
                              <input value={row.value} maxLength={500} onChange={event => handleAdditionalSpecChange(index, 'value', event.target.value)} placeholder="Giá trị" className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" />
                              <button type="button" onClick={() => {
                                const rows = additionalSpecRows.filter((_, rowIndex) => rowIndex !== index);
                                setAdditionalSpecRows(rows);
                                setNewProduct(current => ({ ...current, additionalSpecs: Object.fromEntries(rows.filter(item => item.key.trim()).map(item => [item.key.trim(), item.value])) }));
                              }} title="Xóa thông số" className="rounded border border-rose-950 p-2 text-rose-300"><Minus className="h-4 w-4" /></button>
                            </div>
                          ))}
                        </div>
                      </section>
                    )}
                    <div className="flex gap-2">
                      <button type="submit" className="flex items-center gap-2 rounded bg-cyan-400 px-4 py-2 font-bold text-slate-950"><Save className="h-4 w-4" />{editingProductId ? 'Lưu sản phẩm' : 'Thêm sản phẩm'}</button>
                      {editingProductId && <button type="button" onClick={() => { setEditingProductId(null); setNewProduct(emptyProduct()); setAdditionalSpecRows([]); }} className="rounded border border-cyan-950 px-4 py-2">Hủy sửa</button>}
                    </div>
                  </form>
                  <div className="grid gap-2 rounded-xl border border-cyan-950 bg-[#0c1424] p-3 sm:grid-cols-2 lg:grid-cols-4">
                    <input value={productSearch} onChange={event => setProductSearch(event.target.value)} placeholder="Tìm theo tên hoặc SKU" className="rounded border border-cyan-950 bg-[#111a2e] px-2 py-1.5" />
                    <select value={productCategoryFilter} onChange={event => setProductCategoryFilter(event.target.value)} className="rounded border border-cyan-950 bg-[#111a2e] px-2 py-1.5">
                      <option value="ALL">Tất cả danh mục</option>
                      {categories.map(category => <option key={category.id} value={category.id}>{category.name}</option>)}
                    </select>
                    <select value={productBrandFilter} onChange={event => setProductBrandFilter(event.target.value)} className="rounded border border-cyan-950 bg-[#111a2e] px-2 py-1.5">
                      <option value="ALL">Tất cả thương hiệu</option>
                      {productBrands.map(brand => <option key={brand.id} value={brand.name}>{brand.name}</option>)}
                    </select>
                    <select value={productStatusFilter} onChange={event => setProductStatusFilter(event.target.value)} className="rounded border border-cyan-950 bg-[#111a2e] px-2 py-1.5">
                      <option value="ALL">Tất cả trạng thái</option>
                      <option value="true">Đang bán</option>
                      <option value="false">Đang ẩn</option>
                    </select>
                    <p className="text-[11px] text-slate-400 sm:col-span-2 lg:col-span-4">Tìm thấy {filteredProducts.length} / {products.length} sản phẩm</p>
                  </div>
                  {filteredProducts.length === 0 ? (
                    <AdminEmptyState
                      title={products.length === 0 ? 'Chưa có sản phẩm.' : 'Không tìm thấy sản phẩm phù hợp.'}
                      description={products.length === 0 ? 'Thêm sản phẩm mới bằng biểu mẫu phía trên.' : 'Thử thay đổi từ khóa hoặc bộ lọc sản phẩm.'}
                    />
                  ) : filteredProducts.map(p => (
                    <div key={p.id} className="flex flex-wrap items-center gap-2 bg-[#111a2e] border border-cyan-950 rounded-xl p-3">
                      {p.imageUrl && <img src={p.imageUrl} alt="" className="h-12 w-12 rounded border border-cyan-950 object-contain" />}
                      <span className="font-bold truncate flex-1">{p.name}</span>
                      <span className="text-slate-400">{p.sku}</span>
                      <span className="text-slate-500">{p.categoryName}</span>
                      <span className="text-cyan-400">{p.price?.toLocaleString('vi-VN')}đ</span>
                      {p.promotionalPrice && <span className="text-[10px] text-amber-300">Sale {p.promotionalPrice.toLocaleString('vi-VN')}đ</span>}
                      <span className="text-slate-500">Kho: {p.stockQuantity}</span>
                      <span className={`rounded px-2 py-1 text-[10px] ${p.isActive ? 'bg-emerald-950 text-emerald-300' : 'bg-slate-800 text-slate-400'}`}>{p.isActive ? 'Đang bán' : 'Đang ẩn'}</span>
                      <button type="button" title="Sửa sản phẩm" onClick={() => handleEditProduct(p)} className="rounded border border-cyan-950 p-2 text-cyan-300"><Pencil className="h-4 w-4" /></button>
                      <input
                        value={warehouseLocations[p.id] ?? p.warehouseLocation ?? ''}
                        onChange={event => setWarehouseLocations(current => ({ ...current, [p.id]: event.target.value }))}
                        maxLength={100}
                        placeholder="Vị trí kệ (vd: A-03-02)"
                        aria-label={`Vị trí kho của ${p.name}`}
                        className="w-40 rounded border border-cyan-950 bg-[#0c1424] px-2 py-1 text-[10px] text-slate-200"
                      />
                      <button type="button" onClick={() => handleSaveWarehouseLocation(p)} className="rounded bg-cyan-950 px-2 py-1 text-[10px] text-cyan-300">Lưu vị trí</button>
                      <button type="button" title={p.isActive ? 'Ẩn sản phẩm' : 'Kích hoạt sản phẩm'} onClick={async () => {
                        try {
                          await adminService.setProductActive(p.id, !p.isActive);
                          setProducts(await adminService.getProducts());
                          setMsg(p.isActive ? `Đã ẩn sản phẩm ${p.sku}.` : `Đã kích hoạt sản phẩm ${p.sku}.`);
                        } catch (error) {
                          setMsg(getApiErrorMessage(error, 'Không thể cập nhật trạng thái sản phẩm.'));
                        }
                      }} className={`rounded border p-2 ${p.isActive ? 'border-rose-950 text-rose-400' : 'border-emerald-950 text-emerald-300'}`}>
                        {p.isActive ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
                      </button>
                    </div>
                  ))}
                </div>
              )}

              {tab === 'inventory' && (
                <section className="space-y-4">
                  <div>
                    <h3 className="font-bold text-cyan-300">Tồn kho linh kiện</h3>
                    <p className="mt-1 text-[11px] text-slate-400">Tồn khả dụng đã trừ hàng giữ cho đơn đã xác nhận; nhập kho và điều chỉnh được ghi vào Audit Log.</p>
                  </div>
                  <div className="grid gap-2 rounded-xl border border-cyan-950 bg-[#111a2e] p-3 sm:grid-cols-3">
                    <label className="sr-only" htmlFor="inventory-search">Tìm theo SKU hoặc tên linh kiện</label>
                    <input id="inventory-search" value={inventorySearch} onChange={event => setInventorySearch(event.target.value)} placeholder="Tìm theo tên hoặc SKU" className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" />
                    <label className="sr-only" htmlFor="inventory-category-filter">Lọc theo danh mục</label>
                    <select id="inventory-category-filter" value={inventoryCategoryFilter} onChange={event => setInventoryCategoryFilter(event.target.value)} className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5">
                      <option value="ALL">Tất cả danh mục</option>
                      {categories.map(category => <option key={category.id} value={category.id}>{category.name}</option>)}
                    </select>
                    <label className="sr-only" htmlFor="inventory-status-filter">Lọc theo trạng thái tồn kho</label>
                    <select id="inventory-status-filter" value={inventoryStatusFilter} onChange={event => setInventoryStatusFilter(event.target.value)} className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5">
                      <option value="ALL">Tất cả tình trạng</option>
                      <option value="Available">Còn hàng</option>
                      <option value="LowStock">Sắp hết</option>
                      <option value="OutOfStock">Hết hàng</option>
                    </select>
                    <p className="text-[11px] text-slate-400 sm:col-span-3">Hiển thị {filteredInventory.length} / {inventoryItems.length} linh kiện</p>
                  </div>

                  <div className="space-y-2">
                    {filteredInventory.map(item => (
                      <article key={item.id} className="space-y-3 rounded-xl border border-cyan-950 bg-[#111a2e] p-3">
                        <div className="flex flex-wrap items-start justify-between gap-3">
                          <div className="min-w-48 flex-1">
                            <p className="font-bold text-slate-100">{item.name}</p>
                            <p className="mt-1 text-[10px] text-slate-400">{item.sku} · {item.categoryName}{item.warehouseLocation ? ` · Vị trí: ${item.warehouseLocation}` : ''}</p>
                          </div>
                          <span className={`rounded px-2 py-1 text-[10px] font-bold ${
                            item.status === 'OutOfStock' ? 'bg-rose-950 text-rose-300'
                              : item.status === 'LowStock' ? 'bg-amber-950 text-amber-300'
                                : 'bg-emerald-950 text-emerald-300'
                          }`}>
                            {item.status === 'OutOfStock' ? 'Hết hàng' : item.status === 'LowStock' ? 'Sắp hết hàng' : 'Còn hàng'}
                          </span>
                        </div>

                        <div className="grid grid-cols-2 gap-2 text-[11px] sm:grid-cols-4">
                          <p className="rounded bg-[#0c1424] p-2 text-slate-400">Tồn thực tế <strong className="ml-1 text-slate-100">{item.actualStock}</strong></p>
                          <p className="rounded bg-[#0c1424] p-2 text-slate-400">Đang giữ <strong className="ml-1 text-amber-300">{item.heldQuantity}</strong></p>
                          <p className="rounded bg-[#0c1424] p-2 text-slate-400">Khả dụng <strong className="ml-1 text-cyan-300">{item.availableQuantity}</strong></p>
                          <label className="flex items-center gap-2 rounded bg-[#0c1424] p-2 text-slate-400">
                            Ngưỡng
                            <input type="number" min="0" max="1000000" value={inventoryThresholds[item.id] ?? item.lowStockThreshold} onChange={event => setInventoryThresholds(current => ({ ...current, [item.id]: event.target.value }))} aria-label={`Ngưỡng cảnh báo ${item.sku}`} className="w-16 rounded border border-cyan-950 bg-[#111a2e] px-1.5 py-1 text-slate-100" />
                            <button type="button" onClick={() => handleSaveInventoryThreshold(item)} className="rounded border border-cyan-800 px-2 py-1 text-cyan-300 hover:bg-cyan-950">Lưu</button>
                          </label>
                        </div>

                        <div className="flex gap-2">
                          <button type="button" onClick={() => openInventoryAction('receive', item)} className="flex items-center gap-1 rounded bg-emerald-950 px-3 py-2 text-[11px] font-bold text-emerald-300 hover:bg-emerald-900"><Plus className="h-3.5 w-3.5" />Nhập kho</button>
                          <button type="button" onClick={() => openInventoryAction('adjust', item)} className="flex items-center gap-1 rounded border border-amber-900 px-3 py-2 text-[11px] font-bold text-amber-300 hover:bg-amber-950"><Pencil className="h-3.5 w-3.5" />Điều chỉnh tồn</button>
                        </div>
                      </article>
                    ))}
                    {filteredInventory.length === 0 && (
                      <AdminEmptyState
                        title={inventoryItems.length === 0 ? 'Chưa có dữ liệu tồn kho.' : 'Không tìm thấy linh kiện phù hợp.'}
                        description={inventoryItems.length === 0 ? 'Tồn kho sẽ hiển thị tại đây khi có sản phẩm trong danh mục.' : 'Thử điều chỉnh từ khóa hoặc bộ lọc tồn kho.'}
                      />
                    )}
                  </div>

                  {inventoryAction && (
                    <div className="fixed inset-0 z-[70] flex items-center justify-center bg-black/70 p-3">
                      <form onSubmit={handleSaveInventoryAction} className="w-full max-w-lg space-y-3 rounded-2xl border border-cyan-800 bg-[#0c1424] p-5 shadow-2xl">
                        <div className="flex items-start justify-between gap-3">
                          <div>
                            <h3 className="font-black text-cyan-300">{inventoryAction.type === 'receive' ? 'Nhập hàng vào kho' : 'Điều chỉnh theo kiểm kê'}</h3>
                            <p className="mt-1 text-[11px] text-slate-400">{inventoryAction.item.name} · {inventoryAction.item.sku}</p>
                          </div>
                          <button type="button" onClick={() => setInventoryAction(null)} aria-label="Đóng" className="rounded p-1 text-slate-400 hover:bg-slate-800"><X className="h-4 w-4" /></button>
                        </div>

                        {inventoryAction.type === 'receive' ? (
                          <>
                            <label className="block space-y-1 text-[11px] text-slate-300">Số lượng nhập thêm
                              <input type="number" min="1" step="1" required value={inventoryForm.quantity} onChange={event => setInventoryForm(form => ({ ...form, quantity: event.target.value }))} className="w-full rounded border border-cyan-950 bg-[#111a2e] px-3 py-2 text-sm" />
                            </label>
                            <label className="block space-y-1 text-[11px] text-slate-300">Giá nhập mỗi sản phẩm (VND, không bắt buộc)
                              <input type="number" min="0" step="0.01" value={inventoryForm.unitCost} onChange={event => setInventoryForm(form => ({ ...form, unitCost: event.target.value }))} className="w-full rounded border border-cyan-950 bg-[#111a2e] px-3 py-2 text-sm" />
                            </label>
                            <label className="block space-y-1 text-[11px] text-slate-300">Nhà cung cấp
                              <input maxLength="200" value={inventoryForm.supplier} onChange={event => setInventoryForm(form => ({ ...form, supplier: event.target.value }))} className="w-full rounded border border-cyan-950 bg-[#111a2e] px-3 py-2 text-sm" />
                            </label>
                          </>
                        ) : (
                          <label className="block space-y-1 text-[11px] text-slate-300">Số lượng tồn thực tế sau kiểm kê (bao gồm hàng đang giữ: {inventoryAction.item.heldQuantity})
                            <input type="number" min={inventoryAction.item.heldQuantity} max="2000000000" step="1" required value={inventoryForm.actualStock} onChange={event => setInventoryForm(form => ({ ...form, actualStock: event.target.value }))} className="w-full rounded border border-cyan-950 bg-[#111a2e] px-3 py-2 text-sm" />
                          </label>
                        )}
                        <label className="block space-y-1 text-[11px] text-slate-300">{inventoryAction.type === 'receive' ? 'Ghi chú / lý do nhập' : 'Lý do điều chỉnh'}
                          <textarea maxLength="500" required={inventoryAction.type === 'adjust'} value={inventoryForm.note} onChange={event => setInventoryForm(form => ({ ...form, note: event.target.value }))} rows="3" className="w-full rounded border border-cyan-950 bg-[#111a2e] px-3 py-2 text-sm" />
                        </label>
                        <div className="flex justify-end gap-2">
                          <button type="button" onClick={() => setInventoryAction(null)} className="rounded border border-cyan-950 px-4 py-2 text-slate-300">Hủy</button>
                          <button type="submit" className="rounded bg-cyan-400 px-4 py-2 font-bold text-slate-950">{inventoryAction.type === 'receive' ? 'Xác nhận nhập kho' : 'Xác nhận điều chỉnh'}</button>
                        </div>
                      </form>
                    </div>
                  )}
                </section>
              )}

              {tab === 'promotions' && (
                <div className="space-y-4">
                  <section className="space-y-3 rounded-xl border border-cyan-950 bg-[#111a2e] p-4">
                    <div>
                      <h3 className="font-bold text-cyan-300">Giá bán và giá khuyến mãi trực tiếp</h3>
                      <p className="mt-1 text-[11px] text-slate-400">Chọn sửa một sản phẩm để cập nhật giá niêm yết hoặc thiết lập giá khuyến mãi theo thời gian. Giá hết hạn tự động trở về giá niêm yết.</p>
                    </div>
                    <div className="space-y-2">
                      {products.map(product => (
                        <div key={product.id} className="flex flex-wrap items-center justify-between gap-3 rounded border border-cyan-950 bg-[#0c1424] p-3">
                          <div>
                            <p className="font-bold">{product.name} <span className="text-slate-500">({product.sku})</span></p>
                            <p className="text-[11px] text-slate-400">Giá niêm yết: {product.price.toLocaleString('vi-VN')} đ</p>
                            {product.promotionalPrice && (
                              <p className="text-[11px] text-amber-300">
                                Khuyến mãi: {product.promotionalPrice.toLocaleString('vi-VN')} đ · {new Date(product.promotionStartsAt).toLocaleString('vi-VN')} – {new Date(product.promotionEndsAt).toLocaleString('vi-VN')}
                              </p>
                            )}
                          </div>
                          <button type="button" onClick={() => { handleEditProduct(product); setTab('products'); }} className="flex items-center gap-1.5 rounded border border-cyan-900 px-3 py-2 text-cyan-300"><Pencil className="h-3.5 w-3.5" />Sửa giá</button>
                        </div>
                      ))}
                    </div>
                  </section>

                  <section className="space-y-3 rounded-xl border border-cyan-950 bg-[#111a2e] p-4">
                    <div>
                      <h3 className="font-bold text-cyan-300">Mã giảm giá (Voucher)</h3>
                      <p className="mt-1 text-[11px] text-slate-400">Voucher tự kích hoạt/ngừng theo ngày giờ hiệu lực và tự áp dụng khi khách nhập mã lúc thanh toán.</p>
                    </div>
                    <form onSubmit={handleSaveVoucher} className="grid gap-2 md:grid-cols-2">
                      <input value={newVoucher.code} onChange={event => setNewVoucher({ ...newVoucher, code: event.target.value.toUpperCase() })} maxLength={50} pattern="[A-Za-z0-9-]{3,50}" placeholder="Mã voucher * (VD: PCSTORE10)" className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" required />
                      <select value={newVoucher.discountType} onChange={event => setNewVoucher({ ...newVoucher, discountType: event.target.value, maximumDiscountAmount: '' })} className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5">
                        <option value="Percentage">Giảm theo phần trăm (%)</option>
                        <option value="FixedAmount">Giảm số tiền cố định (VND)</option>
                      </select>
                      <input type="number" min="0.01" max={newVoucher.discountType === 'Percentage' ? '100' : undefined} step="0.01" value={newVoucher.discountValue} onChange={event => setNewVoucher({ ...newVoucher, discountValue: event.target.value })} placeholder={newVoucher.discountType === 'Percentage' ? 'Phần trăm giảm *' : 'Số tiền giảm (VND) *'} className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" required />
                      <input type="number" min="0" step="1" value={newVoucher.minimumOrderAmount} onChange={event => setNewVoucher({ ...newVoucher, minimumOrderAmount: event.target.value })} placeholder="Giá trị đơn tối thiểu (VND)" className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" />
                      {newVoucher.discountType === 'Percentage' && <input type="number" min="1" step="1" value={newVoucher.maximumDiscountAmount} onChange={event => setNewVoucher({ ...newVoucher, maximumDiscountAmount: event.target.value })} placeholder="Mức giảm tối đa (không bắt buộc)" className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" />}
                      <input type="number" min="1" step="1" value={newVoucher.usageLimit} onChange={event => setNewVoucher({ ...newVoucher, usageLimit: event.target.value })} placeholder="Số lượt sử dụng tối đa *" className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" required />
                      <label className="space-y-1 text-[11px] text-slate-300">
                        <span>Ngày giờ bắt đầu *</span>
                        <input type="datetime-local" value={newVoucher.startsAt} onChange={event => setNewVoucher({ ...newVoucher, startsAt: event.target.value })} className="w-full rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" required />
                      </label>
                      <label className="space-y-1 text-[11px] text-slate-300">
                        <span>Ngày giờ kết thúc *</span>
                        <input type="datetime-local" value={newVoucher.endsAt} onChange={event => setNewVoucher({ ...newVoucher, endsAt: event.target.value })} className="w-full rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" required />
                      </label>
                      <div className="flex gap-2 md:col-span-2">
                        <button type="submit" className="rounded bg-cyan-400 px-4 py-2 font-bold text-slate-950">{editingVoucherId ? 'Lưu voucher' : 'Tạo voucher'}</button>
                        {editingVoucherId && <button type="button" onClick={() => { setEditingVoucherId(null); setNewVoucher(emptyVoucher()); }} className="rounded border border-cyan-950 px-4 py-2">Hủy sửa</button>}
                      </div>
                    </form>
                    <div className="space-y-2">
                      {vouchers.map(voucher => {
                        const isInSchedule = currentTime >= new Date(voucher.startsAt).getTime() && currentTime < new Date(voucher.endsAt).getTime();
                        const exhausted = voucher.usageCount >= voucher.usageLimit;
                        return (
                          <div key={voucher.id} className="flex flex-wrap items-center justify-between gap-3 rounded border border-cyan-950 bg-[#0c1424] p-3">
                            <div>
                              <div className="flex flex-wrap items-center gap-2 font-bold">
                                <span className="text-cyan-200">{voucher.code}</span>
                                <span className={`rounded px-2 py-0.5 text-[10px] ${voucher.isActive && isInSchedule && !exhausted ? 'bg-emerald-950 text-emerald-300' : 'bg-slate-800 text-slate-400'}`}>
                                  {!voucher.isActive ? 'Đã ngừng' : exhausted ? 'Hết lượt' : isInSchedule ? 'Đang áp dụng' : 'Theo lịch'}
                                </span>
                              </div>
                              <p className="mt-1 text-[11px] text-slate-400">
                                {voucher.discountType === 'Percentage' ? `${voucher.discountValue}%` : `${voucher.discountValue.toLocaleString('vi-VN')} đ`}
                                {voucher.maximumDiscountAmount && ` · tối đa ${voucher.maximumDiscountAmount.toLocaleString('vi-VN')} đ`}
                                {` · đơn tối thiểu ${voucher.minimumOrderAmount.toLocaleString('vi-VN')} đ · lượt ${voucher.usageCount}/${voucher.usageLimit}`}
                              </p>
                              <p className="text-[10px] text-slate-500">{new Date(voucher.startsAt).toLocaleString('vi-VN')} – {new Date(voucher.endsAt).toLocaleString('vi-VN')}</p>
                            </div>
                            <div className="flex gap-2">
                              <button type="button" title="Sửa voucher" onClick={() => handleEditVoucher(voucher)} className="rounded border border-cyan-950 p-2 text-cyan-300"><Pencil className="h-4 w-4" /></button>
                              <button type="button" title={voucher.isActive ? 'Ngừng voucher' : 'Kích hoạt voucher'} onClick={() => handleToggleVoucher(voucher)} className="rounded border border-cyan-950 p-2 text-slate-300">{voucher.isActive ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}</button>
                            </div>
                          </div>
                        );
                      })}
                    </div>
                  </section>
                </div>
              )}

              {tab === 'categories' && (
                <div className="space-y-4">
                  <section className="space-y-3 rounded-xl border border-cyan-950 bg-[#111a2e] p-4">
                    <h3 className="font-bold text-cyan-300">Danh mục linh kiện</h3>
                    <form onSubmit={handleCreateCategory} className="grid gap-2 md:grid-cols-2">
                      <input placeholder="Tên danh mục" maxLength={100} value={newCategory.name} onChange={e => setNewCategory({ ...newCategory, name: e.target.value })} className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" required />
                      <select value={newCategory.componentType} onChange={e => setNewCategory({ ...newCategory, componentType: e.target.value })} className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5">
                        {['CPU', 'Mainboard', 'RAM', 'GPU', 'SSD', 'HDD', 'PSU', 'Case', 'Cooler', 'Monitor', 'Other'].map(type => <option key={type} value={type}>{type}</option>)}
                      </select>
                      <input placeholder="Mô tả" maxLength={500} value={newCategory.description} onChange={e => setNewCategory({ ...newCategory, description: e.target.value })} className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" />
                      <input type="number" min="0" step="1" aria-label="Thứ tự hiển thị danh mục" placeholder="Thứ tự hiển thị" value={newCategory.displayOrder} onChange={e => setNewCategory({ ...newCategory, displayOrder: Number(e.target.value) })} className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" />
                      <p className="text-xs text-slate-400 md:col-span-2">Slug SEO tự tạo: <span className="text-cyan-300">{newCategory.name.trim().toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'se-duoc-tao-tu-ten'}</span></p>
                      <div className="flex gap-2 md:col-span-2">
                        <button type="submit" className="rounded bg-cyan-400 px-4 py-2 font-bold text-slate-950">{editingCategoryId ? 'Lưu danh mục' : 'Thêm danh mục'}</button>
                        {editingCategoryId && <button type="button" onClick={() => { setEditingCategoryId(null); setNewCategory({ name: '', componentType: 'CPU', description: '', displayOrder: 0 }); }} className="rounded border border-cyan-950 px-4 py-2">Hủy sửa</button>}
                      </div>
                    </form>
                    <div className="space-y-2">
                      {categories.map(category => (
                        <div key={category.id} className="flex flex-wrap items-center justify-between gap-3 rounded border border-cyan-950 bg-[#0c1424] p-3">
                          <div className="min-w-0">
                            <div className="flex flex-wrap items-center gap-2 font-bold">
                              <span>{category.name}</span><span className="text-xs text-cyan-400">{category.type}</span>
                              <span className={`rounded px-2 py-0.5 text-[10px] ${category.isActive ? 'bg-emerald-950 text-emerald-300' : 'bg-slate-800 text-slate-400'}`}>{category.isActive ? 'Đang hiện' : 'Đang ẩn'}</span>
                            </div>
                            <p className="mt-1 text-xs text-slate-400">/{category.slug} · Thứ tự {category.displayOrder} · {category.productCount} sản phẩm</p>
                            {category.description && <p className="text-xs text-slate-500">{category.description}</p>}
                          </div>
                          <div className="flex gap-2">
                            <button type="button" title="Sửa danh mục" onClick={() => { setEditingCategoryId(category.id); setNewCategory({ name: category.name, componentType: category.type, description: category.description || '', displayOrder: category.displayOrder }); }} className="rounded border border-cyan-950 p-2 text-cyan-300"><Pencil className="h-4 w-4" /></button>
                            <button type="button" title={category.isActive ? 'Ẩn danh mục' : 'Hiện danh mục'} onClick={() => handleToggleCategory(category)} className="rounded border border-cyan-950 p-2 text-slate-300">{category.isActive ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}</button>
                          </div>
                        </div>
                      ))}
                    </div>
                  </section>

                  <section className="space-y-3 rounded-xl border border-cyan-950 bg-[#111a2e] p-4">
                    <h3 className="flex items-center gap-2 font-bold text-cyan-300"><Tag className="h-4 w-4" />Thương hiệu</h3>
                    <form onSubmit={handleSaveBrand} className="grid gap-2 md:grid-cols-2">
                      <input placeholder="Tên thương hiệu" maxLength={100} value={newBrand.name} onChange={e => setNewBrand({ ...newBrand, name: e.target.value })} className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" required />
                      <input placeholder="URL logo (không bắt buộc)" type="url" maxLength={500} value={newBrand.logoUrl} onChange={e => setNewBrand({ ...newBrand, logoUrl: e.target.value })} className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" />
                      <input placeholder="Mô tả" maxLength={500} value={newBrand.description} onChange={e => setNewBrand({ ...newBrand, description: e.target.value })} className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" />
                      <input type="number" min="0" step="1" aria-label="Thứ tự hiển thị thương hiệu" placeholder="Thứ tự hiển thị" value={newBrand.displayOrder} onChange={e => setNewBrand({ ...newBrand, displayOrder: Number(e.target.value) })} className="rounded border border-cyan-950 bg-[#0c1424] px-2 py-1.5" />
                      <p className="text-xs text-slate-400 md:col-span-2">Slug SEO tự tạo: <span className="text-cyan-300">{newBrand.name.trim().toLowerCase().normalize('NFD').replace(/[\u0300-\u036f]/g, '').replace(/đ/g, 'd').replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '') || 'se-duoc-tao-tu-ten'}</span></p>
                      <div className="flex gap-2 md:col-span-2">
                        <button type="submit" className="rounded bg-cyan-400 px-4 py-2 font-bold text-slate-950">{editingBrandId ? 'Lưu thương hiệu' : 'Thêm thương hiệu'}</button>
                        {editingBrandId && <button type="button" onClick={() => { setEditingBrandId(null); setNewBrand({ name: '', logoUrl: '', description: '', displayOrder: 0 }); }} className="rounded border border-cyan-950 px-4 py-2">Hủy sửa</button>}
                      </div>
                    </form>
                    <div className="space-y-2">
                      {brands.map(brand => (
                        <div key={brand.id} className="flex flex-wrap items-center justify-between gap-3 rounded border border-cyan-950 bg-[#0c1424] p-3">
                          <div className="flex min-w-0 items-center gap-3">
                            {brand.logoUrl && <img src={brand.logoUrl} alt="" className="h-8 w-8 rounded object-contain" />}
                            <div>
                              <div className="flex flex-wrap items-center gap-2 font-bold">
                                <span>{brand.name}</span>
                                <span className={`rounded px-2 py-0.5 text-[10px] ${brand.isActive ? 'bg-emerald-950 text-emerald-300' : 'bg-slate-800 text-slate-400'}`}>{brand.isActive ? 'Đang hiện' : 'Đang ẩn'}</span>
                              </div>
                              <p className="mt-1 text-xs text-slate-400">/{brand.slug} · Thứ tự {brand.displayOrder} · {brand.productCount} sản phẩm</p>
                              {brand.description && <p className="text-xs text-slate-500">{brand.description}</p>}
                            </div>
                          </div>
                          <div className="flex gap-2">
                            <button type="button" title="Sửa thương hiệu" onClick={() => { setEditingBrandId(brand.id); setNewBrand({ name: brand.name, logoUrl: brand.logoUrl || '', description: brand.description || '', displayOrder: brand.displayOrder }); }} className="rounded border border-cyan-950 p-2 text-cyan-300"><Pencil className="h-4 w-4" /></button>
                            <button type="button" title={brand.isActive ? 'Ẩn thương hiệu' : 'Hiện thương hiệu'} onClick={() => handleToggleBrand(brand)} className="rounded border border-cyan-950 p-2 text-slate-300">{brand.isActive ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}</button>
                          </div>
                        </div>
                      ))}
                    </div>
                  </section>
                </div>
              )}

              {tab === 'users' && (
                <div className="space-y-4">
                  <div className="rounded-xl border border-cyan-950 bg-[#111a2e] p-3">
                    <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
                      <div className="flex-1">
                        <label className="block text-[10px] uppercase tracking-[0.2em] text-slate-400">Tìm nhân viên</label>
                        <input value={employeeSearch} onChange={event => setEmployeeSearch(event.target.value)} placeholder="Tên, email hoặc số điện thoại..." className="mt-1 w-full rounded border border-cyan-950 bg-[#0c1424] px-3 py-2 text-xs text-slate-200" />
                      </div>
                      <div className="md:w-48">
                        <label className="block text-[10px] uppercase tracking-[0.2em] text-slate-400">Vai trò</label>
                        <select value={employeeRoleFilter} onChange={event => setEmployeeRoleFilter(event.target.value)} className="mt-1 w-full rounded border border-cyan-950 bg-[#0c1424] px-3 py-2 text-xs text-slate-200">
                          <option value="ALL">Tất cả</option>
                          <option value="Staff">Nhân viên</option>
                          <option value="Manager">Quản lý</option>
                          <option value="Admin">Admin</option>
                        </select>
                      </div>
                      <button type="button" onClick={exportEmployeeReport} className="rounded bg-cyan-400 px-3 py-2 text-[10px] font-bold text-slate-950">Xuất báo cáo CSV</button>
                    </div>
                  </div>

                  {selectedEmployee && (
                    <div className="rounded-xl border border-cyan-950 bg-[#111a2e] p-4">
                      <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
                        <div>
                          <p className="text-[10px] uppercase tracking-[0.2em] text-cyan-300">Báo cáo hiệu suất</p>
                          <h3 className="mt-1 text-lg font-black text-white">{selectedEmployee.fullName}</h3>
                          <p className="text-[11px] text-slate-400">{selectedEmployee.email} · {selectedEmployee.role}</p>
                        </div>
                        <div className="flex gap-2">
                          {['Staff', 'Manager', 'Admin'].map(role => (
                            <button key={role} onClick={() => handleUpdateRole(selectedEmployee.id, role)}
                              className={`rounded border px-2 py-1 text-[10px] font-bold ${selectedEmployee.role === role ? 'border-cyan-400 bg-cyan-950 text-cyan-300' : 'border-cyan-950 text-slate-400'}`}>
                              {role}
                            </button>
                          ))}
                          <button onClick={() => handleToggleActive(selectedEmployee.id)} className="rounded border border-rose-800 bg-rose-950 px-2 py-1 text-[10px] font-bold text-rose-200">{selectedEmployee.isActive ? 'Khóa' : 'Mở'}</button>
                        </div>
                      </div>

                      <div className="mt-4 grid gap-3 md:grid-cols-4">
                        {[
                          { label: 'Số đơn xử lý', value: selectedEmployee.totalHandledOrders ?? 0 },
                          { label: 'Đơn thành công', value: selectedEmployee.successfulOrders ?? 0 },
                          { label: 'Tỷ lệ thành công', value: `${Number(selectedEmployee.successRate ?? 0).toFixed(1)}%` },
                          { label: 'TG đóng gói TB', value: `${Number(selectedEmployee.avgPackagingMinutes ?? 0).toFixed(1)} phút` },
                        ].map(metric => (
                          <div key={metric.label} className="rounded border border-cyan-950 bg-[#0c1424] p-3">
                            <p className="text-[10px] uppercase tracking-[0.2em] text-slate-500">{metric.label}</p>
                            <p className="mt-2 text-xl font-black text-cyan-300">{metric.value}</p>
                          </div>
                        ))}
                      </div>

                      <div className="mt-4">
                        <p className="mb-2 text-[10px] uppercase tracking-[0.2em] text-slate-400">Năng suất xử lý</p>
                        <div className="h-3 overflow-hidden rounded-full bg-slate-800">
                          <div className="h-full rounded-full bg-gradient-to-r from-emerald-400 to-cyan-400" style={{ width: `${Math.min(Number(selectedEmployee.successRate ?? 0), 100)}%` }} />
                        </div>
                        <p className="mt-2 text-[10px] text-slate-400">Cập nhật cuối: {selectedEmployee.lastHandledAt ? new Date(selectedEmployee.lastHandledAt).toLocaleString('vi-VN') : 'Chưa có hoạt động'} </p>
                      </div>
                    </div>
                  )}

                  <div className="space-y-2">
                    {filteredEmployees.length === 0 ? (
                      <div className="rounded-xl border border-dashed border-cyan-950 bg-[#111a2e] p-8 text-center text-slate-400">Không tìm thấy nhân viên phù hợp.</div>
                    ) : filteredEmployees.map(user => (
                      <button key={user.id} type="button" onClick={() => setSelectedEmployeeId(user.id)} className={`w-full rounded-xl border p-3 text-left transition ${selectedEmployee?.id === user.id ? 'border-cyan-400 bg-[#111a2e]' : 'border-cyan-950 bg-[#0c1424]'}`}>
                        <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
                          <div>
                            <div className="flex flex-wrap items-center gap-2">
                              <span className="font-bold text-white">{user.fullName}</span>
                              <span className={`rounded px-2 py-0.5 text-[10px] font-bold ${user.role === 'Admin' ? 'bg-violet-950 text-violet-300' : user.role === 'Manager' ? 'bg-amber-950 text-amber-300' : 'bg-cyan-950 text-cyan-300'}`}>{user.role}</span>
                              <span className={`text-[10px] ${user.isActive ? 'text-emerald-400' : 'text-rose-400'}`}>{user.isActive ? 'Đang hoạt động' : 'Bị khóa'}</span>
                            </div>
                            <p className="mt-1 text-[11px] text-slate-400">{user.email} · {user.phoneNumber || 'Chưa có số điện thoại'}</p>
                          </div>
                          <div className="flex flex-wrap gap-3 text-[10px] text-slate-300">
                            <span>Đơn xử lý: <strong className="text-white">{user.totalHandledOrders ?? 0}</strong></span>
                            <span>Thành công: <strong className="text-emerald-300">{user.successfulOrders ?? 0}</strong></span>
                            <span>Tỷ lệ: <strong className="text-cyan-300">{Number(user.successRate ?? 0).toFixed(1)}%</strong></span>
                          </div>
                        </div>
                      </button>
                    ))}
                  </div>
                </div>
              )}

              {tab === 'accounts' && (
                <section className="space-y-4">
                  <header className="rounded-xl border border-cyan-950 bg-[#111a2e] p-4">
                    <div className="flex items-center gap-3">
                      <div className="rounded-lg bg-cyan-950 p-2 text-cyan-300"><ShieldCheck className="h-5 w-5" /></div>
                      <div>
                        <h3 className="font-bold text-cyan-200">Quản lý tài khoản toàn hệ thống</h3>
                        <p className="mt-1 text-[10px] text-slate-400">Tìm kiếm, phân quyền, khóa/mở khóa và đặt lại mật khẩu. Mật khẩu tạm chỉ gửi qua email, không hiển thị trên màn hình.</p>
                      </div>
                    </div>
                  </header>

                  <form onSubmit={handleCreateAccount} className="space-y-3 rounded-xl border border-cyan-950 bg-[#111a2e] p-4">
                    <h4 className="flex items-center gap-2 font-bold text-cyan-300"><UserPlus className="h-4 w-4" /> Tạo tài khoản nhân viên/quản lý</h4>
                    <div className="grid gap-2 md:grid-cols-2">
                      <label className="text-[10px] text-slate-400">Họ tên *
                        <input required maxLength={100} value={accountForm.fullName} onChange={event => setAccountForm(form => ({ ...form, fullName: event.target.value }))} className="mt-1 w-full rounded border border-cyan-950 bg-[#0c1424] px-3 py-2 text-xs text-slate-100" />
                      </label>
                      <label className="text-[10px] text-slate-400">Email đăng nhập *
                        <input required type="email" maxLength={150} value={accountForm.email} onChange={event => setAccountForm(form => ({ ...form, email: event.target.value }))} className="mt-1 w-full rounded border border-cyan-950 bg-[#0c1424] px-3 py-2 text-xs text-slate-100" />
                      </label>
                      <label className="text-[10px] text-slate-400">Số điện thoại
                        <input type="tel" maxLength={20} value={accountForm.phoneNumber} onChange={event => setAccountForm(form => ({ ...form, phoneNumber: event.target.value }))} className="mt-1 w-full rounded border border-cyan-950 bg-[#0c1424] px-3 py-2 text-xs text-slate-100" />
                      </label>
                      <label className="text-[10px] text-slate-400">Vai trò
                        <select value={accountForm.role} onChange={event => setAccountForm(form => ({ ...form, role: event.target.value }))} className="mt-1 w-full rounded border border-cyan-950 bg-[#0c1424] px-3 py-2 text-xs text-slate-100">
                          <option value="Staff">Nhân viên</option>
                          <option value="Manager">Quản lý</option>
                        </select>
                      </label>
                    </div>
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <p className="text-[10px] text-slate-500">Hệ thống tự tạo mật khẩu ngẫu nhiên, mã hóa trước khi lưu và gửi thông tin đăng nhập qua email.</p>
                      <button type="submit" disabled={accountActionLoading} className="flex items-center gap-2 rounded bg-cyan-400 px-4 py-2 text-[10px] font-bold text-slate-950 disabled:opacity-50">
                        {accountActionLoading ? <Loader2 className="h-4 w-4 animate-spin" /> : <UserPlus className="h-4 w-4" />}
                        Tạo tài khoản
                      </button>
                    </div>
                  </form>

                  <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-amber-800 bg-amber-950/30 p-4">
                    <div>
                      <h4 className="font-bold text-amber-200">Admin bảo vệ (không thể thay đổi quyền)</h4>
                      <p className="mt-1 text-[10px] text-amber-100/70">
                        {PROTECTED_ADMIN_EMAIL} · quyền Admin luôn được giữ cố định; không thể đổi vai trò, khóa hoặc đặt lại mật khẩu từ trang quản lý.
                      </p>
                    </div>
                    <button
                      type="button"
                      disabled={accountActionLoading || accounts.some(account => account.email.toLowerCase() === PROTECTED_ADMIN_EMAIL)}
                      onClick={handleCreateProtectedAdmin}
                      className="flex items-center gap-2 rounded bg-amber-400 px-4 py-2 text-[10px] font-bold text-slate-950 disabled:cursor-not-allowed disabled:opacity-50"
                    >
                      {accountActionLoading ? <Loader2 className="h-4 w-4 animate-spin" /> : <ShieldCheck className="h-4 w-4" />}
                      {accounts.some(account => account.email.toLowerCase() === PROTECTED_ADMIN_EMAIL) ? 'Đã tồn tại' : 'Tạo Admin bảo vệ'}
                    </button>
                  </div>

                  <div className="grid gap-3 rounded-xl border border-cyan-950 bg-[#111a2e] p-3 md:grid-cols-[1fr_180px_180px]">
                    <label className="text-[10px] text-slate-400">Tìm theo tên, email hoặc số điện thoại
                      <input value={accountSearch} onChange={event => setAccountSearch(event.target.value)} placeholder="Nhập từ khóa..." className="mt-1 w-full rounded border border-cyan-950 bg-[#0c1424] px-3 py-2 text-xs text-slate-200" />
                    </label>
                    <label className="text-[10px] text-slate-400">Lọc vai trò
                      <select value={accountRoleFilter} onChange={event => setAccountRoleFilter(event.target.value)} className="mt-1 w-full rounded border border-cyan-950 bg-[#0c1424] px-3 py-2 text-xs text-slate-200">
                        <option value="ALL">Tất cả vai trò</option>
                        <option value="Admin">Admin</option>
                        <option value="Manager">Quản lý</option>
                        <option value="Staff">Nhân viên</option>
                        <option value="Customer">Khách hàng</option>
                      </select>
                    </label>
                    <label className="text-[10px] text-slate-400">Lọc trạng thái
                      <select value={accountStatusFilter} onChange={event => setAccountStatusFilter(event.target.value)} className="mt-1 w-full rounded border border-cyan-950 bg-[#0c1424] px-3 py-2 text-xs text-slate-200">
                        <option value="ALL">Tất cả trạng thái</option>
                        <option value="Active">Đang hoạt động</option>
                        <option value="Locked">Đang khóa</option>
                      </select>
                    </label>
                  </div>

                  {selectedAccount && (
                    <article className="rounded-xl border border-cyan-800 bg-[#111a2e] p-4">
                      <div className="flex flex-wrap items-start justify-between gap-4">
                        <div>
                          <p className="text-[10px] uppercase tracking-wider text-cyan-300">Thông tin tài khoản</p>
                          <h4 className="mt-1 text-base font-black text-white">{selectedAccount.fullName}</h4>
                          <p className="mt-1 text-[11px] text-slate-300">{selectedAccount.email} · {selectedAccount.phoneNumber || 'Chưa có số điện thoại'}</p>
                          <p className="mt-1 text-[10px] text-slate-500">Tạo ngày {new Date(selectedAccount.createdAt).toLocaleString('vi-VN')} · ID {selectedAccount.id}</p>
                        </div>
                        <div className="flex flex-wrap items-end gap-2">
                          <label className="text-[10px] text-slate-400">Vai trò
                            <select
                              value={selectedAccount.role}
                              disabled={selectedAccount.id === currentUserId || selectedAccount.isProtectedAdmin || accountActionLoading}
                              onChange={event => handleAccountRoleChange(selectedAccount, event.target.value)}
                              className="mt-1 block rounded border border-cyan-900 bg-[#0c1424] px-3 py-2 text-xs text-slate-100 disabled:opacity-50"
                            >
                              <option value="Customer">Khách hàng</option>
                              <option value="Staff">Nhân viên</option>
                              <option value="Manager">Quản lý</option>
                              {selectedAccount.role === 'Admin' && <option value="Admin">Admin</option>}
                            </select>
                          </label>
                          <button
                            type="button"
                            disabled={selectedAccount.id === currentUserId || selectedAccount.isProtectedAdmin || accountActionLoading}
                            onClick={() => handleAccountStatusToggle(selectedAccount)}
                            className={`rounded border px-3 py-2 text-[10px] font-bold disabled:opacity-50 ${selectedAccount.isActive ? 'border-rose-800 bg-rose-950 text-rose-200' : 'border-emerald-800 bg-emerald-950 text-emerald-200'}`}
                          >
                            {selectedAccount.isActive ? 'Khóa tài khoản' : 'Mở khóa'}
                          </button>
                          <button
                            type="button"
                            disabled={selectedAccount.id === currentUserId || selectedAccount.isProtectedAdmin || accountActionLoading}
                            onClick={() => handleResetAccountPassword(selectedAccount)}
                            className="flex items-center gap-1 rounded border border-amber-700 bg-amber-950 px-3 py-2 text-[10px] font-bold text-amber-200 disabled:opacity-50"
                          >
                            <KeyRound className="h-3.5 w-3.5" /> Đặt lại mật khẩu
                          </button>
                        </div>
                      </div>
                      {selectedAccount.id === currentUserId && <p className="mt-3 rounded-lg border border-amber-800 bg-amber-950/50 p-2 text-[10px] text-amber-200">Không thể đổi vai trò, khóa hoặc đặt lại mật khẩu của Admin đang đăng nhập.</p>}
                      {selectedAccount.isProtectedAdmin && <p className="mt-3 rounded-lg border border-amber-800 bg-amber-950/50 p-2 text-[10px] text-amber-200">Tài khoản Admin bảo vệ: quyền Admin không thể thay đổi; các thao tác đổi vai trò, khóa và đặt lại mật khẩu đều bị khóa.</p>}
                    </article>
                  )}

                  <div className="space-y-2">
                    {filteredAccounts.length === 0 ? (
                      <div className="rounded-xl border border-dashed border-cyan-950 bg-[#111a2e] p-8 text-center text-slate-400">Không tìm thấy tài khoản phù hợp.</div>
                    ) : filteredAccounts.map(account => (
                      <button key={account.id} type="button" onClick={() => setSelectedAccountId(account.id)} className={`w-full rounded-xl border p-3 text-left transition ${selectedAccount?.id === account.id ? 'border-cyan-400 bg-[#111a2e]' : 'border-cyan-950 bg-[#0c1424]'}`}>
                        <div className="flex flex-wrap items-center justify-between gap-3">
                          <div>
                            <div className="flex flex-wrap items-center gap-2">
                              <span className="font-bold text-white">{account.fullName}</span>
                              <span className={`rounded px-2 py-0.5 text-[10px] font-bold ${account.role === 'Admin' ? 'bg-violet-950 text-violet-300' : account.role === 'Manager' ? 'bg-amber-950 text-amber-300' : account.role === 'Staff' ? 'bg-cyan-950 text-cyan-300' : 'bg-slate-800 text-slate-300'}`}>{account.role}</span>
                              <span className={`text-[10px] ${account.isActive ? 'text-emerald-400' : 'text-rose-400'}`}>{account.isActive ? 'Đang hoạt động' : 'Đang khóa'}</span>
                              {account.id === currentUserId && <span className="rounded bg-amber-950 px-2 py-0.5 text-[9px] text-amber-200">Tài khoản hiện tại</span>}
                            </div>
                            <p className="mt-1 text-[11px] text-slate-400">{account.email} · {account.phoneNumber || 'Chưa có số điện thoại'}</p>
                          </div>
                          <span className="text-[10px] text-slate-500">Tạo {new Date(account.createdAt).toLocaleDateString('vi-VN')}</span>
                        </div>
                      </button>
                    ))}
                  </div>
                </section>
              )}

              {tab === 'permissions' && permissionMatrix && (
                <section className="space-y-4">
                  <header className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
                    <h3 className="font-bold text-slate-800">Cấu hình phân quyền vai trò</h3>
                    <p className="mt-1 text-xs text-slate-500">
                      {permissionMatrix.isProtected
                        ? 'Admin có toàn bộ quyền hệ thống. Các quyền cốt lõi này được bảo vệ và chỉ xem.'
                        : 'Thay đổi có hiệu lực ngay cho các yêu cầu API tiếp theo.'}
                    </p>
                  </header>
                  <label className="block max-w-sm text-xs font-medium text-slate-600">Vai trò cần cấu hình
                    <select
                      value={permissionRole}
                      onChange={event => setPermissionRole(event.target.value)}
                      className="mt-1 w-full rounded-xl border border-slate-300 bg-white px-3 py-2 text-xs text-slate-800"
                    >
                      <option value="Customer">Khách hàng</option>
                      <option value="Staff">Nhân viên</option>
                      <option value="Manager">Quản lý</option>
                      <option value="Admin">Admin</option>
                    </select>
                  </label>
                  <div className="overflow-x-auto rounded-2xl border border-slate-200 bg-white shadow-sm">
                    <table className="w-full min-w-[720px] text-left">
                      <thead className="bg-slate-100 text-[10px] text-slate-600">
                        <tr>
                          <th className="px-3 py-3">Chức năng</th>
                          {permissionMatrix.actions.map(action => <th key={action.key} className="px-3 py-3 text-center">{action.label}</th>)}
                        </tr>
                      </thead>
                      <tbody>
                        {permissionMatrix.modules.map(module => (
                          <tr key={module.key} className="border-t border-slate-200">
                            <th className="px-3 py-3 font-semibold text-slate-700">{module.label}</th>
                            {permissionMatrix.actions.map(action => {
                              const grant = permissionMatrix.permissions.find(item => item.module === module.key && item.action === action.key);
                              return (
                                <td key={action.key} className="px-3 py-3 text-center">
                                  <input
                                    aria-label={`${action.label}: ${module.label}`}
                                    type="checkbox"
                                    checked={Boolean(grant?.isAllowed)}
                                    disabled={permissionMatrix.isProtected}
                                    onChange={event => setPermissionMatrix(current => ({
                                      ...current,
                                      permissions: current.permissions.map(item => item.module === module.key && item.action === action.key
                                        ? { ...item, isAllowed: event.target.checked }
                                        : item),
                                    }))}
                                    className="h-4 w-4 accent-blue-600"
                                  />
                                </td>
                              );
                            })}
                          </tr>
                        ))}
                      </tbody>
                    </table>
                  </div>
                  <button
                    type="button"
                    disabled={permissionSaving || permissionMatrix.isProtected}
                    onClick={async () => {
                      setPermissionSaving(true);
                      try {
                        const result = await adminService.updateRolePermissions(permissionRole, {
                          role: permissionRole,
                          permissions: permissionMatrix.permissions,
                        });
                        setMsg(result.message);
                      } catch (error) {
                        setMsg(getApiErrorMessage(error, 'Không thể lưu phân quyền.'));
                      } finally {
                        setPermissionSaving(false);
                      }
                    }}
                    className="flex items-center gap-2 rounded-xl bg-blue-600 px-4 py-2 font-bold text-white shadow-sm transition hover:bg-blue-700 disabled:opacity-50"
                  >
                    {permissionSaving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
                    Lưu phân quyền
                  </button>
                </section>
              )}

              {tab === 'audit-logs' && (
                <section className="space-y-4">
                  <header className="flex flex-wrap items-start justify-between gap-3 rounded-xl border border-cyan-950 bg-[#111a2e] p-4">
                    <div>
                      <h3 className="flex items-center gap-2 font-bold text-cyan-200"><FileClock className="h-4 w-4" /> Nhật ký hoạt động hệ thống</h3>
                      <p className="mt-1 text-[10px] text-slate-400">Chỉ xem, không chỉnh sửa hoặc xóa. Nhật ký mới nhất hiển thị trước; mỗi tệp xuất tối đa 10.000 bản ghi theo bộ lọc.</p>
                    </div>
                    <div className="flex gap-2">
                      {[
                        ['csv', 'Xuất CSV'],
                        ['excel', 'Xuất Excel'],
                      ].map(([format, label]) => (
                        <button
                          key={format}
                          type="button"
                          disabled={auditExporting}
                          onClick={async () => {
                            setAuditExporting(true);
                            try {
                              const blob = await adminService.exportAuditLogs(
                                auditRequestFilters(auditFilters),
                                format,
                              );
                              downloadBlob(blob, `audit-logs-${Date.now()}.${format === 'excel' ? 'xls' : 'csv'}`);
                              setMsg(`Đã xuất nhật ký dạng ${format.toUpperCase()}.`);
                            } catch (error) {
                              setMsg(getApiErrorMessage(error, 'Không thể xuất nhật ký hoạt động.'));
                            } finally {
                              setAuditExporting(false);
                            }
                          }}
                          className="flex items-center gap-1.5 rounded border border-cyan-800 bg-cyan-950 px-3 py-2 text-[10px] font-bold text-cyan-200 disabled:opacity-50"
                        >
                          {auditExporting ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Download className="h-3.5 w-3.5" />}
                          {label}
                        </button>
                      ))}
                    </div>
                  </header>

                  <form
                    onSubmit={event => {
                      event.preventDefault();
                      setAuditPage(1);
                      setAuditFilters({ ...auditDraft });
                    }}
                    className="grid gap-3 rounded-xl border border-cyan-950 bg-[#111a2e] p-3 md:grid-cols-3"
                  >
                    <label className="text-[10px] text-slate-400">Từ ngày
                      <input type="date" value={auditDraft.fromDate} onChange={event => setAuditDraft(current => ({ ...current, fromDate: event.target.value }))} className="mt-1 w-full rounded border border-cyan-950 bg-[#0c1424] px-3 py-2 text-xs text-slate-200" />
                    </label>
                    <label className="text-[10px] text-slate-400">Đến ngày
                      <input type="date" value={auditDraft.toDate} onChange={event => setAuditDraft(current => ({ ...current, toDate: event.target.value }))} className="mt-1 w-full rounded border border-cyan-950 bg-[#0c1424] px-3 py-2 text-xs text-slate-200" />
                    </label>
                    <label className="text-[10px] text-slate-400">Tài khoản thực hiện
                      <input value={auditDraft.user} onChange={event => setAuditDraft(current => ({ ...current, user: event.target.value }))} placeholder="Email hoặc tài khoản" className="mt-1 w-full rounded border border-cyan-950 bg-[#0c1424] px-3 py-2 text-xs text-slate-200" />
                    </label>
                    <label className="text-[10px] text-slate-400">Loại hành động
                      <input value={auditDraft.action} onChange={event => setAuditDraft(current => ({ ...current, action: event.target.value }))} placeholder="LOGIN, UPDATE_PRODUCT_PRICE..." className="mt-1 w-full rounded border border-cyan-950 bg-[#0c1424] px-3 py-2 text-xs text-slate-200" />
                    </label>
                    <label className="text-[10px] text-slate-400">Trạng thái
                      <select value={auditDraft.status} onChange={event => setAuditDraft(current => ({ ...current, status: event.target.value }))} className="mt-1 w-full rounded border border-cyan-950 bg-[#0c1424] px-3 py-2 text-xs text-slate-200">
                        <option value="">Tất cả</option>
                        <option value="Success">Thành công</option>
                        <option value="Failed">Thất bại</option>
                      </select>
                    </label>
                    <div className="flex items-end gap-2">
                      <button type="submit" className="flex items-center gap-2 rounded bg-cyan-400 px-4 py-2 text-[10px] font-bold text-slate-950"><Search className="h-3.5 w-3.5" /> Lọc dữ liệu</button>
                      <button type="button" onClick={() => {
                        const emptyFilters = { fromDate: '', toDate: '', user: '', action: '', status: '' };
                        setAuditDraft(emptyFilters);
                        setAuditFilters(emptyFilters);
                        setAuditPage(1);
                      }} className="rounded border border-cyan-900 px-3 py-2 text-[10px] font-bold text-slate-300">Xóa lọc</button>
                    </div>
                  </form>

                  <div className="overflow-x-auto rounded-xl border border-cyan-950">
                    <table className="w-full min-w-[1050px] text-left">
                      <thead className="bg-[#111a2e] text-[10px] text-cyan-200">
                        <tr>
                          <th className="px-3 py-3">Mã log</th>
                          <th className="px-3 py-3">Thời gian</th>
                          <th className="px-3 py-3">Người thực hiện</th>
                          <th className="px-3 py-3">Vai trò</th>
                          <th className="px-3 py-3">Hành động</th>
                          <th className="px-3 py-3">Địa chỉ IP</th>
                          <th className="px-3 py-3">Trạng thái</th>
                          <th className="px-3 py-3">Chi tiết</th>
                        </tr>
                      </thead>
                      <tbody>
                        {auditPageData.items.map(log => (
                          <tr key={log.id} className="border-t border-cyan-950 text-[10px] text-slate-300">
                            <td className="px-3 py-3">#{log.id}</td>
                            <td className="px-3 py-3 whitespace-nowrap">{new Date(log.timestamp).toLocaleString('vi-VN')}</td>
                            <td className="px-3 py-3">{log.performedBy || 'Anonymous'}</td>
                            <td className="px-3 py-3">{log.performedByRole || '—'}</td>
                            <td className="px-3 py-3"><span className="font-semibold text-cyan-200">{log.action || '—'}</span><span className="block text-[9px] text-slate-500">{log.module || '—'}</span></td>
                            <td className="px-3 py-3">{log.ipAddress || '—'}</td>
                            <td className="px-3 py-3"><span className={`rounded px-2 py-1 font-bold ${log.status === 'Success' ? 'bg-emerald-950 text-emerald-300' : 'bg-rose-950 text-rose-300'}`}>{log.status === 'Success' ? 'Thành công' : 'Thất bại'}</span></td>
                            <td className="px-3 py-3">
                              <button type="button" onClick={() => setSelectedAuditLog(log)} className="inline-flex items-center gap-1 rounded border border-cyan-900 px-2 py-1 text-cyan-300 hover:bg-cyan-950"><Eye className="h-3 w-3" /> Xem</button>
                            </td>
                          </tr>
                        ))}
                        {auditPageData.items.length === 0 && (
                          <tr><td colSpan={8} className="px-3 py-10 text-center text-slate-500">Không có nhật ký khớp với điều kiện lọc.</td></tr>
                        )}
                      </tbody>
                    </table>
                  </div>

                  <footer className="flex flex-wrap items-center justify-between gap-3 text-[10px] text-slate-400">
                    <span>{auditPageData.totalCount.toLocaleString('vi-VN')} bản ghi · Hiển thị tối đa 50 bản ghi mỗi trang</span>
                    <div className="flex items-center gap-2">
                      <button type="button" disabled={auditPage <= 1} onClick={() => setAuditPage(page => page - 1)} className="rounded border border-cyan-900 px-3 py-1.5 disabled:opacity-40">Trang trước</button>
                      <span>Trang {auditPage} / {Math.max(1, Math.ceil(auditPageData.totalCount / auditPageData.pageSize))}</span>
                      <button type="button" disabled={auditPage >= Math.ceil(auditPageData.totalCount / auditPageData.pageSize)} onClick={() => setAuditPage(page => page + 1)} className="rounded border border-cyan-900 px-3 py-1.5 disabled:opacity-40">Trang sau</button>
                    </div>
                  </footer>

                  {selectedAuditLog && (
                    <div className="fixed inset-0 z-[70] flex items-center justify-center bg-black/70 p-4" role="presentation" onClick={() => setSelectedAuditLog(null)}>
                      <section role="dialog" aria-modal="true" aria-labelledby="audit-detail-title" onClick={event => event.stopPropagation()} className="max-h-[90vh] w-full max-w-3xl overflow-y-auto rounded-2xl border border-cyan-900 bg-[#0c1424] p-5 shadow-2xl">
                        <header className="mb-4 flex items-center justify-between">
                          <div>
                            <h3 id="audit-detail-title" className="font-black text-white">Chi tiết nhật ký #{selectedAuditLog.id}</h3>
                            <p className="mt-1 text-[10px] text-slate-400">{new Date(selectedAuditLog.timestamp).toLocaleString('vi-VN')}</p>
                          </div>
                          <button type="button" aria-label="Đóng chi tiết nhật ký" onClick={() => setSelectedAuditLog(null)} className="rounded p-2 text-slate-400 hover:bg-slate-800 hover:text-white"><X className="h-4 w-4" /></button>
                        </header>
                        <dl className="grid gap-3 sm:grid-cols-2">
                          {[
                            ['Người thực hiện', selectedAuditLog.performedBy || 'Anonymous'],
                            ['Vai trò', selectedAuditLog.performedByRole || '—'],
                            ['Hành động', selectedAuditLog.action || '—'],
                            ['Module', selectedAuditLog.module || '—'],
                            ['Địa chỉ IP', selectedAuditLog.ipAddress || '—'],
                            ['Trạng thái', selectedAuditLog.status === 'Success' ? 'Thành công' : 'Thất bại'],
                          ].map(([label, value]) => <div key={label} className="rounded-lg border border-cyan-950 bg-[#111a2e] p-3"><dt className="text-[9px] text-slate-500">{label}</dt><dd className="mt-1 break-all text-xs text-slate-200">{value}</dd></div>)}
                          <div className="rounded-lg border border-cyan-950 bg-[#111a2e] p-3 sm:col-span-2"><dt className="text-[9px] text-slate-500">User-Agent / thiết bị</dt><dd className="mt-1 break-all text-[10px] text-slate-200">{selectedAuditLog.userAgent || 'Không có dữ liệu'}</dd></div>
                        </dl>
                        {(() => {
                          const details = readAuditDetails(selectedAuditLog.details);
                          if (!details || typeof details !== 'object') return null;
                          const oldValue = details.oldValue ?? details.old ?? details.before ?? details.from;
                          const newValue = details.newValue ?? details.new ?? details.after ?? details.to;
                          return oldValue !== undefined || newValue !== undefined ? (
                            <div className="mt-4 grid gap-3 sm:grid-cols-2">
                              <div className="rounded-lg border border-rose-950 bg-rose-950/20 p-3"><h4 className="mb-2 text-[10px] font-bold text-rose-300">Dữ liệu trước khi sửa</h4><pre className="whitespace-pre-wrap break-words text-[10px] text-slate-300">{JSON.stringify(oldValue ?? null, null, 2)}</pre></div>
                              <div className="rounded-lg border border-emerald-950 bg-emerald-950/20 p-3"><h4 className="mb-2 text-[10px] font-bold text-emerald-300">Dữ liệu sau khi sửa</h4><pre className="whitespace-pre-wrap break-words text-[10px] text-slate-300">{JSON.stringify(newValue ?? null, null, 2)}</pre></div>
                            </div>
                          ) : null;
                        })()}
                        <div className="mt-4 rounded-lg border border-cyan-950 bg-[#111a2e] p-3">
                          <h4 className="mb-2 text-[10px] font-bold text-cyan-200">Nội dung chi tiết / lỗi</h4>
                          <pre className="max-h-64 overflow-auto whitespace-pre-wrap break-words text-[10px] text-slate-300">{JSON.stringify(readAuditDetails(selectedAuditLog.details), null, 2) || 'Không có nội dung bổ sung.'}</pre>
                        </div>
                      </section>
                    </div>
                  )}
                </section>
              )}

              {tab === 'settings' && (
                <div className="space-y-5">
                  <section className="rounded-2xl border border-cyan-900 bg-[#0a1220] p-4 sm:p-5">
                    <div className="mb-4 flex items-center gap-3">
                      <Database className="h-5 w-5 text-cyan-300" />
                      <div>
                        <h3 className="font-bold text-white">Kết nối PostgreSQL</h3>
                        <p className="text-[11px] text-slate-400">Thông tin bí mật được mã hóa; để trống mật khẩu để giữ mật khẩu đã lưu.</p>
                      </div>
                    </div>
                    <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
                      {[
                        ['Host', 'host', 'text'],
                        ['Port', 'port', 'number'],
                        ['Tên database', 'database', 'text'],
                        ['Username', 'username', 'text'],
                        ['Mật khẩu', 'password', 'password'],
                      ].map(([label, field, type]) => (
                        <label key={field} className="space-y-1 text-xs font-semibold text-slate-300">
                          <span>{label}</span>
                          <input
                            type={type}
                            min={type === 'number' ? 1 : undefined}
                            max={type === 'number' ? 65535 : undefined}
                            value={systemConfiguration.database[field]}
                            onChange={event => updateSystemConfigurationField(
                              'database',
                              field,
                              type === 'number' ? Number(event.target.value) : event.target.value,
                            )}
                            placeholder={field === 'password' && systemConfigurationStatus?.databasePasswordConfigured ? 'Đã lưu (để trống để giữ nguyên)' : ''}
                            className="w-full rounded-lg border border-cyan-950 bg-[#111a2e] px-3 py-2 text-sm text-white outline-none focus:border-cyan-500"
                          />
                        </label>
                      ))}
                    </div>
                    {systemConfigurationStatus?.databaseConnectionChangePendingRestart && (
                      <p className="mt-3 rounded-lg border border-amber-800 bg-amber-950/30 p-3 text-xs text-amber-200">
                        Thay đổi database đang chờ áp dụng. Cần khởi động lại API; kết nối hiện tại vẫn đang hoạt động.
                      </p>
                    )}
                    {configurationHealth.database && (
                      <p className={`mt-3 rounded-lg border p-3 text-xs ${healthStatusClass(configurationHealth.database)}`}>
                        {configurationHealth.database.message}
                        {configurationHealth.database.latencyMs != null && ` (${Math.round(configurationHealth.database.latencyMs)} ms)`}
                      </p>
                    )}
                    <button
                      type="button"
                      onClick={handleTestSystemDatabase}
                      disabled={Boolean(configurationAction)}
                      className="mt-4 rounded-lg border border-cyan-700 px-4 py-2 text-xs font-bold text-cyan-200 hover:bg-cyan-950 disabled:opacity-50"
                    >
                      {configurationAction === 'database' ? 'Đang kiểm tra...' : 'Kiểm tra kết nối database'}
                    </button>
                  </section>

                  <section className="rounded-2xl border border-cyan-900 bg-[#0a1220] p-4 sm:p-5">
                    <div className="mb-4 flex items-center gap-3">
                      <KeyRound className="h-5 w-5 text-cyan-300" />
                      <div>
                        <h3 className="font-bold text-white">Nhà cung cấp AI và tham số RAG</h3>
                        <p className="text-[11px] text-slate-400">API Key không được trả về giao diện; nhập key mới nếu muốn thay key đã lưu.</p>
                      </div>
                    </div>
                    <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
                      <label className="space-y-1 text-xs font-semibold text-slate-300 sm:col-span-2">
                        <span>LLM API Key {systemConfigurationStatus?.aiApiKeyConfigured ? '(đã cấu hình)' : '(chưa cấu hình)'}</span>
                        <input
                          type="password"
                          autoComplete="new-password"
                          value={systemConfiguration.ai.apiKey}
                          onChange={event => updateSystemConfigurationField('ai', 'apiKey', event.target.value)}
                          placeholder={systemConfigurationStatus?.aiApiKeyConfigured ? 'Để trống để giữ key đã lưu' : 'Nhập API Key'}
                          className="w-full rounded-lg border border-cyan-950 bg-[#111a2e] px-3 py-2 text-sm text-white outline-none focus:border-cyan-500"
                        />
                      </label>
                      {[
                        ['Model', 'model', 'text', undefined, undefined],
                        ['Temperature', 'temperature', 'number', 0, 2],
                        ['Số token tối đa', 'maxTokens', 'number', 1, 8192],
                        ['RAG Top-K', 'ragTopK', 'number', 1, 100],
                        ['RAG Threshold', 'ragThreshold', 'number', 0, 1],
                      ].map(([label, field, type, min, max]) => (
                        <label key={field} className="space-y-1 text-xs font-semibold text-slate-300">
                          <span>{label}</span>
                          <input
                            type={type}
                            min={min}
                            max={max}
                            step={field === 'temperature' || field === 'ragThreshold' ? 0.05 : 1}
                            value={systemConfiguration.ai[field]}
                            onChange={event => updateSystemConfigurationField(
                              'ai',
                              field,
                              type === 'number' ? Number(event.target.value) : event.target.value,
                            )}
                            className="w-full rounded-lg border border-cyan-950 bg-[#111a2e] px-3 py-2 text-sm text-white outline-none focus:border-cyan-500"
                          />
                        </label>
                      ))}
                    </div>
                    {configurationHealth.ai && (
                      <p className={`mt-3 rounded-lg border p-3 text-xs ${healthStatusClass(configurationHealth.ai)}`}>
                        {configurationHealth.ai.message}
                        {configurationHealth.ai.latencyMs != null && ` (${Math.round(configurationHealth.ai.latencyMs)} ms)`}
                      </p>
                    )}
                    <button
                      type="button"
                      onClick={handleTestSystemAi}
                      disabled={Boolean(configurationAction)}
                      className="mt-4 rounded-lg border border-cyan-700 px-4 py-2 text-xs font-bold text-cyan-200 hover:bg-cyan-950 disabled:opacity-50"
                    >
                      {configurationAction === 'ai' ? 'Đang kiểm tra...' : 'Kiểm tra API AI'}
                    </button>
                    <p className="mt-3 text-[10px] leading-relaxed text-slate-500">
                      Các tham số RAG được lưu để cấu hình truy xuất; thao tác lưu áp dụng ngay cho tư vấn AI, còn đổi database cần khởi động lại API.
                    </p>
                  </section>

                  <div className="flex flex-wrap justify-end gap-2">
                    <button
                      type="button"
                      onClick={handleCancelSystemConfiguration}
                      disabled={Boolean(configurationAction)}
                      className="rounded-lg border border-slate-700 px-4 py-2 text-xs font-bold text-slate-300 disabled:opacity-50"
                    >
                      Hủy thay đổi
                    </button>
                    <button
                      type="button"
                      onClick={handleSaveSystemConfiguration}
                      disabled={Boolean(configurationAction)}
                      className="flex items-center gap-2 rounded-lg bg-cyan-400 px-4 py-2 text-xs font-bold text-slate-950 disabled:opacity-50"
                    >
                      {configurationAction === 'save' ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
                      Lưu cấu hình hệ thống
                    </button>
                  </div>

                  {settings.length > 0 && (
                    <details className="rounded-xl border border-cyan-950 p-4">
                      <summary className="cursor-pointer text-sm font-bold text-slate-300">Cấu hình phụ</summary>
                      <div className="mt-4 space-y-3">
                        {settings.map((setting, index) => (
                          <div key={setting.key} className="flex items-center gap-3">
                            <label htmlFor={`legacy-setting-${index}`} className="w-32 break-all text-xs font-bold text-slate-400">{setting.key}</label>
                            <input
                              id={`legacy-setting-${index}`}
                              value={setting.value}
                              onChange={event => {
                                const updated = [...settings];
                                updated[index] = { ...setting, value: event.target.value };
                                setSettings(updated);
                              }}
                              className="flex-1 rounded border border-cyan-950 bg-[#111a2e] px-3 py-1.5 text-sm"
                            />
                          </div>
                        ))}
                        <button onClick={handleSaveSettings} className="flex items-center gap-2 rounded-xl bg-cyan-400 px-4 py-2 font-bold text-slate-950">
                          <Save className="h-4 w-4" /> Lưu cấu hình phụ
                        </button>
                      </div>
                    </details>
                  )}
                </div>
              )}

              {tab === 'backup' && (
                <section className="space-y-4">
                  <header className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
                    <h3 className="font-bold text-slate-800">Sao lưu và phục hồi dữ liệu</h3>
                    <p className="mt-1 text-xs leading-relaxed text-slate-500">
                      Bản sao lưu chứa đầy đủ cơ sở dữ liệu và được lưu trên máy chủ. Phục hồi sẽ thay thế dữ liệu hiện tại;
                      hệ thống tự tạo bản sao an toàn trước khi bắt đầu. API cần có công cụ PostgreSQL `pg_dump` và `pg_restore`;
                      mặc định file được lưu trong `PCStore.API/App_Data/Backups`.
                    </p>
                  </header>

                  <div className="grid gap-3 sm:grid-cols-2">
                    <article className="rounded-2xl border border-blue-100 bg-gradient-to-br from-blue-50 to-white p-4 shadow-sm">
                      <div className="flex items-center gap-2 text-blue-700">
                        <Database className="h-4 w-4" />
                        <h4 className="font-bold">Tạo bản sao lưu</h4>
                      </div>
                      <p className="mt-2 text-xs text-slate-500">Lưu một bản dump đầy đủ của cơ sở dữ liệu hiện tại.</p>
                      <button
                        type="button"
                        onClick={handleBackup}
                        disabled={Boolean(backupAction)}
                        className="mt-4 flex items-center gap-2 rounded-xl bg-blue-600 px-4 py-2 text-xs font-bold text-white shadow-sm transition hover:bg-blue-700 disabled:opacity-50"
                      >
                        {backupAction === 'create' ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
                        {backupAction === 'create' ? 'Đang sao lưu...' : 'Sao lưu dữ liệu ngay'}
                      </button>
                    </article>

                    <article className="rounded-2xl border border-amber-100 bg-gradient-to-br from-amber-50 to-white p-4 shadow-sm">
                      <div className="flex items-center gap-2 text-amber-700">
                        <RefreshCw className="h-4 w-4" />
                        <h4 className="font-bold">Phục hồi bản gần nhất</h4>
                      </div>
                      <p className="mt-2 text-xs text-slate-500">
                        {latestRestorableBackup
                          ? `Bản mới nhất: ${new Date(latestRestorableBackup.createdAtUtc).toLocaleString('vi-VN')} · ${formatFileSize(latestRestorableBackup.sizeBytes)}`
                          : 'Chưa có bản sao lưu. Hãy tạo bản sao lưu trước khi phục hồi.'}
                      </p>
                      <button
                        type="button"
                        onClick={() => { setRestoreTarget(latestRestorableBackup); setRestoreConfirmation(''); setRestoreDialogOpen(true); }}
                        disabled={!latestRestorableBackup || Boolean(backupAction)}
                        className="mt-4 flex items-center gap-2 rounded-xl border border-amber-300 bg-white px-4 py-2 text-xs font-bold text-amber-800 transition hover:bg-amber-100 disabled:cursor-not-allowed disabled:opacity-50"
                      >
                        <RefreshCw className="h-4 w-4" />
                        Phục hồi bản gần nhất
                      </button>
                    </article>
                  </div>

                  <div className="overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm">
                    <div className="border-b border-slate-100 px-4 py-3">
                      <h4 className="text-xs font-bold text-slate-700">Các bản sao lưu trên máy chủ</h4>
                    </div>
                    {backups.length === 0 ? (
                      <div className="p-4">
                        <AdminEmptyState icon={Database} title="Chưa có bản sao lưu." description="Tạo bản sao lưu để có thể phục hồi dữ liệu sau này." />
                      </div>
                    ) : (
                      <div className="divide-y divide-slate-100">
                        {backups.map(backup => (
                          <div key={backup.id} className="flex flex-wrap items-center justify-between gap-2 px-4 py-3">
                            <div className="min-w-0">
                              <p className="text-xs font-semibold text-slate-700">
                                {backup.isSafetyBackup ? 'Bản an toàn trước phục hồi' : backup.id === latestRestorableBackup?.id ? 'Bản gần nhất' : 'Bản sao lưu'}
                                {' · '}{new Date(backup.createdAtUtc).toLocaleString('vi-VN')}
                              </p>
                              <p className="mt-1 text-[10px] text-slate-400">{backup.id}</p>
                            </div>
                            <div className="flex shrink-0 items-center gap-2">
                              <span className="rounded-full bg-slate-100 px-2.5 py-1 text-[10px] font-semibold text-slate-600">{formatFileSize(backup.sizeBytes)}</span>
                              <button
                                type="button"
                                onClick={() => { setRestoreTarget(backup); setRestoreConfirmation(''); setRestoreDialogOpen(true); }}
                                disabled={Boolean(backupAction)}
                                className="rounded-lg border border-slate-200 px-2.5 py-1.5 text-[10px] font-bold text-slate-600 hover:border-amber-300 hover:bg-amber-50 hover:text-amber-800 disabled:opacity-50"
                              >
                                Phục hồi bản này
                              </button>
                            </div>
                          </div>
                        ))}
                      </div>
                    )}
                  </div>

                  {restoreDialogOpen && (
                    <div className="fixed inset-0 z-[80] flex items-center justify-center bg-slate-950/55 p-3 backdrop-blur-sm">
                      <section role="dialog" aria-modal="true" aria-labelledby="restore-backup-title" className="w-full max-w-lg space-y-4 rounded-3xl border border-slate-200 bg-white p-5 shadow-2xl sm:p-6">
                        <div>
                          <p className="text-[10px] font-black uppercase tracking-[0.18em] text-rose-600">Thao tác ghi đè dữ liệu</p>
                          <h3 id="restore-backup-title" className="mt-1 text-lg font-black text-slate-900">Phục hồi bản sao lưu gần nhất?</h3>
                          <p className="mt-2 text-xs leading-relaxed text-slate-600">
                            Toàn bộ dữ liệu hiện tại sẽ được thay thế bằng {restoreTarget?.isSafetyBackup ? 'bản an toàn' : 'bản sao lưu'} lúc {restoreTarget ? new Date(restoreTarget.createdAtUtc).toLocaleString('vi-VN') : ''}.
                            Hệ thống sẽ tự tạo bản sao an toàn của dữ liệu hiện tại trước khi phục hồi.
                          </p>
                        </div>
                        <label className="block text-xs font-semibold text-slate-700">
                          Nhập <span className="font-black text-rose-600">PHUC HOI</span> để xác nhận
                          <input
                            autoFocus
                            value={restoreConfirmation}
                            onChange={event => setRestoreConfirmation(event.target.value)}
                            className="mt-1 w-full rounded-xl border border-slate-300 bg-white px-3 py-2 text-sm text-slate-800"
                          />
                        </label>
                        <div className="flex justify-end gap-2">
                          <button
                            type="button"
                            onClick={() => { setRestoreDialogOpen(false); setRestoreConfirmation(''); setRestoreTarget(null); }}
                            disabled={backupAction === 'restore'}
                            className="rounded-xl border border-slate-200 px-4 py-2 text-xs font-bold text-slate-600 hover:bg-slate-50 disabled:opacity-50"
                          >
                            Hủy
                          </button>
                          <button
                            type="button"
                            onClick={handleRestoreBackup}
                            disabled={restoreConfirmation !== 'PHUC HOI' || Boolean(backupAction)}
                            className="flex items-center gap-2 rounded-xl bg-rose-600 px-4 py-2 text-xs font-bold text-white hover:bg-rose-700 disabled:opacity-50"
                          >
                            {backupAction === 'restore' && <Loader2 className="h-4 w-4 animate-spin" />}
                            {backupAction === 'restore' ? 'Đang phục hồi...' : 'Xác nhận phục hồi'}
                          </button>
                        </div>
                      </section>
                    </div>
                  )}
                </section>
              )}
            </>
          )}
        </div>
      </main>
    </div>
    {documentOrder && (
      <OrderDocumentPreview
        orderId={documentOrder.orderId}
        documentType={documentOrder.documentType}
        staffName={staffName}
        onClose={() => setDocumentOrder(null)}
      />
    )}
  </div>
  );
}
