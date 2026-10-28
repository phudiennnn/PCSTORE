import axios from 'axios';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

const API_BASE_URL = 'http://127.0.0.1:5170/api';

const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: { 'Content-Type': 'application/json' },
  timeout: 30000,
});

apiClient.interceptors.request.use(config => {
    const accessToken = localStorage.getItem('pcstore_access_token');
    if (accessToken) config.headers.Authorization = `Bearer ${accessToken}`;
    return config;
});

export const getApiErrorMessage = (error, fallback) => {
    if (!error?.response) {
        return error?.code === 'ERR_NETWORK'
            ? 'Không kết nối được API. Hãy kiểm tra dịch vụ backend.'
            : error?.message || fallback;
    }

    const { status, data } = error.response;
    const detail = data?.message || data?.detail || data?.title;
    if (typeof detail === 'string' && detail.trim()) return detail;
    if (status === 401) return 'Phiên đăng nhập đã hết hạn hoặc chưa xác thực. Vui lòng đăng nhập lại.';
    if (status === 403) return 'Tài khoản của bạn không có quyền thực hiện chức năng này.';
    if (status === 404) return 'Không tìm thấy API xử lý đơn. Hãy khởi động lại backend để nạp phiên bản mới.';
    return `${fallback} (HTTP ${status}).`;
};

export const authService = {
    register: async (userData) => (await apiClient.post('/auth/register', userData)).data,
    login: async (credentials) => (await apiClient.post('/auth/login', credentials)).data,
    logout: async () => (await apiClient.post('/auth/logout')).data,
    requestPasswordReset: async (data) => (await apiClient.post('/auth/password-reset/request', data)).data,
    resetPassword: async (data) => (await apiClient.post('/auth/password-reset/confirm', data)).data,
    getPermissions: async () => (await apiClient.get('/auth/permissions')).data,
    getProfile: async (userId) => (await apiClient.get(`/users/profile/${userId}`)).data,
    updateProfile: async (userId, data) => (await apiClient.put(`/users/profile/${userId}`, data)).data,
    changePassword: async (userId, data) => (await apiClient.post(`/users/change-password/${userId}`, data)).data,
};

export const productService = {
    getProducts: async (filters = {}) => {
        const { searchTerm, categoryType, minPrice, maxPrice, sortBy } = filters;
        const params = {};
        if (searchTerm) params.searchTerm = searchTerm;
        if (categoryType && categoryType !== 'ALL') params.categoryType = categoryType;
        if (minPrice !== undefined && minPrice !== '') params.minPrice = minPrice;
        if (maxPrice !== undefined && maxPrice !== '') params.maxPrice = maxPrice;
        if (sortBy) params.sortBy = sortBy;
        return (await apiClient.get('/products', { params })).data;
    },
    getCategories: async () => (await apiClient.get('/products/categories')).data,
    getBrands: async () => (await apiClient.get('/products/brands')).data,
    getProductById: async (id) => (await apiClient.get(`/products/${id}`)).data,
    compareProducts: async (ids) => (await apiClient.get('/products/compare', { params: { ids: ids.join(',') } })).data,
};

export const reviewService = {
    getReviews: async (productId) => (await apiClient.get(`/products/${productId}/reviews`)).data,
    createReview: async (productId, data) => (await apiClient.post(`/products/${productId}/reviews`, data)).data,
};

export const orderService = {
    createOrder: async (data) => (await apiClient.post('/orders', data)).data,
    validateVoucher: async (code, orderSubtotal) =>
        (await apiClient.post('/vouchers/validate', { code, orderSubtotal })).data,
    getUserOrders: async (userId) => (await apiClient.get(`/orders/user/${userId}`)).data,
    getUserOrderById: async (userId, orderId) => (await apiClient.get(`/orders/user/${userId}/${orderId}`)).data,
    submitOrderActionRequest: async (orderId, requestType, reason) =>
        (await apiClient.post(`/orders/${orderId}/requests`, { requestType, reason })).data,
    getOrderActionRequest: async (orderId, requestId) =>
        (await apiClient.get(`/orders/${orderId}/requests/${requestId}`)).data,
    lookupOrder: async (orderCode, receiverPhone) =>
        (await apiClient.get('/orders/lookup', { params: { orderCode, receiverPhone } })).data,
    getOrderById: async (id) => (await apiClient.get(`/orders/${id}`)).data,
    getAllOrders: async (status) => (await apiClient.get('/orders', { params: status ? { status } : {} })).data,
    getOrderActionRequests: async (status = 'Pending') =>
        (await apiClient.get('/admin/order-requests', { params: status && status !== 'ALL' ? { status } : {} })).data,
    reviewOrderActionRequest: async (requestId, approve, note) =>
        (await apiClient.post(`/admin/order-requests/${requestId}/review`, { approve, note })).data,
    confirmOrder: async (id, changedByName) =>
        (await apiClient.post(`/orders/${id}/confirm`, { changedByName })).data,
    updateStatus: async (id, status, { trackingNumber, carrier, note, changedByName } = {}) =>
        (await apiClient.put(`/orders/${id}/status`, { status, trackingNumber, carrier, note, changedByName })).data,
    recordDocumentPrint: async (id, documentType, changedByName) =>
        (await apiClient.post(`/orders/${id}/documents/print`, { documentType, changedByName })).data,
    connectToNewOrderNotifications: async (onNewOrder, onOrderConfirmed, onOrderStatusUpdated, onInventoryUpdated, onOrderActionRequested) => {
        const connection = new HubConnectionBuilder()
            .withUrl(`${API_BASE_URL.replace(/\/api$/, '')}/hubs/orders`)
            .withAutomaticReconnect()
            .configureLogging(LogLevel.Warning)
            .build();
        if (onNewOrder) connection.on('OrderCreated', onNewOrder);
        if (onOrderConfirmed) connection.on('OrderConfirmed', onOrderConfirmed);
        if (onOrderStatusUpdated) connection.on('OrderStatusUpdated', onOrderStatusUpdated);
        if (onInventoryUpdated) connection.on('InventoryUpdated', onInventoryUpdated);
        if (onOrderActionRequested) connection.on('OrderActionRequested', onOrderActionRequested);
        await connection.start();
        return connection;
    },
};

export const aiService = {
    consult: async (message) => (await apiClient.post('/ai/consult', { message })).data,
    recommendBuild: async (data) => (await apiClient.post('/ai/build-recommendation', data)).data,
    getUpgradeSuggestions: async (productIds) => (await apiClient.get('/ai/upgrade-suggestions', { params: { productIds: productIds.join(',') } })).data,
};

export const staffService = {
    searchCustomers: async (search) => (await apiClient.get('/staff/customers', { params: { search } })).data,
    getCustomerHistory: async (customerId) => (await apiClient.get(`/staff/customers/${customerId}`)).data,
    checkPcCompatibility: async (productIds, config = {}) =>
        (await apiClient.post('/staff/compatibility-check', { productIds }, config)).data,
    getSupportTickets: async () => (await apiClient.get('/staff/support-tickets')).data,
};

export const pcBuildService = {
    saveBuild: async (userId, buildName, items, isCompatible, purpose = 'general') =>
        (await apiClient.post(`/builds/user/${userId}`, { buildName, items, isCompatible, purpose })).data,
};

export const adminService = {
    getStats: async (fromDate, toDate, categoryId) => (
        await apiClient.get('/admin/stats', { params: { fromDate, toDate, categoryId } })
    ).data,
    recordReportExport: async data => (await apiClient.post('/admin/reports/export', data)).data,
    getUsers: async () => (await apiClient.get('/admin/users')).data,
    getAccounts: async (filters = {}) => (await apiClient.get('/admin/accounts', { params: filters })).data,
    createAccount: async data => (await apiClient.post('/admin/accounts', data)).data,
    resetAccountPassword: async id => (await apiClient.post(`/admin/accounts/${id}/reset-password`)).data,
    updateUserRole: async (id, role) => (await apiClient.put(`/admin/users/${id}/role`, { role })).data,
    toggleUserActive: async (id) => (await apiClient.put(`/admin/users/${id}/toggle-active`)).data,
    getCategories: async () => (await apiClient.get('/admin/categories')).data,
    createCategory: async (data) => (await apiClient.post('/admin/categories', data)).data,
    updateCategory: async (id, data) => (await apiClient.put(`/admin/categories/${id}`, data)).data,
    setCategoryActive: async (id, isActive) => (await apiClient.patch(`/admin/categories/${id}/active`, { isActive })).data,
    getBrands: async () => (await apiClient.get('/admin/brands')).data,
    createBrand: async (data) => (await apiClient.post('/admin/brands', data)).data,
    updateBrand: async (id, data) => (await apiClient.put(`/admin/brands/${id}`, data)).data,
    setBrandActive: async (id, isActive) => (await apiClient.patch(`/admin/brands/${id}/active`, { isActive })).data,
    getProducts: async () => (await apiClient.get('/admin/products')).data,
    getInventory: async (filters = {}) => (await apiClient.get('/admin/inventory', { params: filters })).data,
    receiveInventory: async (id, data) => (await apiClient.post(`/admin/inventory/${id}/receive`, data)).data,
    adjustInventory: async (id, data) => (await apiClient.post(`/admin/inventory/${id}/adjust`, data)).data,
    updateLowStockThreshold: async (id, threshold) =>
        (await apiClient.patch(`/admin/inventory/${id}/threshold`, { threshold })).data,
    getVouchers: async () => (await apiClient.get('/admin/vouchers')).data,
    createVoucher: async (data) => (await apiClient.post('/admin/vouchers', data)).data,
    updateVoucher: async (id, data) => (await apiClient.put(`/admin/vouchers/${id}`, data)).data,
    setVoucherActive: async (id, isActive) => (await apiClient.patch(`/admin/vouchers/${id}/active`, { isActive })).data,
    createProduct: async (data) => (await apiClient.post('/admin/products', data)).data,
    updateProduct: async (id, data) => (await apiClient.put(`/admin/products/${id}`, data)).data,
    setProductActive: async (id, isActive) => (await apiClient.patch(`/admin/products/${id}/active`, { isActive })).data,
    updateStock: async (id, stockQuantity) => (await apiClient.put(`/admin/products/${id}/stock`, { stockQuantity })).data,
    updateWarehouseLocation: async (id, warehouseLocation) =>
        (await apiClient.put(`/admin/products/${id}/warehouse-location`, { warehouseLocation })).data,
    updatePrice: async (id, price) => (await apiClient.put(`/admin/products/${id}/price`, { price })).data,
    deleteProduct: async (id) => (await apiClient.delete(`/admin/products/${id}`)).data,
    getSettings: async () => (await apiClient.get('/admin/settings')).data,
    updateSettings: async (settings) => (await apiClient.put('/admin/settings', settings)).data,
    getSystemConfiguration: async () => (await apiClient.get('/admin/system-configuration')).data,
    testSystemDatabase: async database => (await apiClient.post('/admin/system-configuration/test-database', database)).data,
    testSystemAi: async ai => (await apiClient.post('/admin/system-configuration/test-ai', ai)).data,
    updateSystemConfiguration: async configuration =>
        (await apiClient.put('/admin/system-configuration', configuration)).data,
    getBackups: async () => (await apiClient.get('/admin/backups', { timeout: 0 })).data,
    createBackup: async () => (await apiClient.post('/admin/backups', {}, { timeout: 0 })).data,
    restoreBackup: async backupId =>
        (await apiClient.post(`/admin/backups/${encodeURIComponent(backupId)}/restore`, {}, { timeout: 0 })).data,
    getRolePermissions: async role => (await apiClient.get(`/admin/role-permissions/${role}`)).data,
    updateRolePermissions: async (role, data) => (await apiClient.put(`/admin/role-permissions/${role}`, data)).data,
    getAuditLogs: async filters => (await apiClient.get('/admin/audit-logs', { params: filters })).data,
    exportAuditLogs: async (filters, format) => (await apiClient.get('/admin/audit-logs/export', {
        params: { ...filters, format },
        responseType: 'blob',
    })).data,
};

export default apiClient;
