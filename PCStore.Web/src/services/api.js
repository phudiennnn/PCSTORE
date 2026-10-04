import axios from 'axios';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';

const API_BASE_URL = 'http://127.0.0.1:5170/api';

const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: { 'Content-Type': 'application/json' },
  timeout: 30000,
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
    if (status === 404) return 'Không tìm thấy API xử lý đơn. Hãy khởi động lại backend để nạp phiên bản mới.';
    return `${fallback} (HTTP ${status}).`;
};

export const authService = {
    register: async (userData) => (await apiClient.post('/auth/register', userData)).data,
    login: async (credentials) => (await apiClient.post('/auth/login', credentials)).data,
    logout: async () => (await apiClient.post('/auth/logout')).data,
    requestPasswordReset: async (data) => (await apiClient.post('/auth/password-reset/request', data)).data,
    resetPassword: async (data) => (await apiClient.post('/auth/password-reset/confirm', data)).data,
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
    getProductById: async (id) => (await apiClient.get(`/products/${id}`)).data,
    compareProducts: async (ids) => (await apiClient.get('/products/compare', { params: { ids: ids.join(',') } })).data,
};

export const reviewService = {
    getReviews: async (productId) => (await apiClient.get(`/products/${productId}/reviews`)).data,
    createReview: async (productId, data) => (await apiClient.post(`/products/${productId}/reviews`, data)).data,
};

export const orderService = {
    createOrder: async (data) => (await apiClient.post('/orders', data)).data,
    getUserOrders: async (userId) => (await apiClient.get(`/orders/user/${userId}`)).data,
    getUserOrderById: async (userId, orderId) => (await apiClient.get(`/orders/user/${userId}/${orderId}`)).data,
    getOrderById: async (id) => (await apiClient.get(`/orders/${id}`)).data,
    getAllOrders: async (status) => (await apiClient.get('/orders', { params: status ? { status } : {} })).data,
    confirmOrder: async (id, changedByName) =>
        (await apiClient.post(`/orders/${id}/confirm`, { changedByName })).data,
    updateStatus: async (id, status, { trackingNumber, carrier, note, changedByName } = {}) =>
        (await apiClient.put(`/orders/${id}/status`, { status, trackingNumber, carrier, note, changedByName })).data,
    connectToNewOrderNotifications: async (onNewOrder, onOrderConfirmed, onOrderStatusUpdated) => {
        const connection = new HubConnectionBuilder()
            .withUrl(`${API_BASE_URL.replace(/\/api$/, '')}/hubs/orders`)
            .withAutomaticReconnect()
            .configureLogging(LogLevel.Warning)
            .build();
        if (onNewOrder) connection.on('OrderCreated', onNewOrder);
        if (onOrderConfirmed) connection.on('OrderConfirmed', onOrderConfirmed);
        if (onOrderStatusUpdated) connection.on('OrderStatusUpdated', onOrderStatusUpdated);
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
    getSupportTickets: async () => (await apiClient.get('/staff/support-tickets')).data,
};

export const adminService = {
    getStats: async () => (await apiClient.get('/admin/stats')).data,
    getUsers: async () => (await apiClient.get('/admin/users')).data,
    updateUserRole: async (id, role) => (await apiClient.put(`/admin/users/${id}/role`, { role })).data,
    toggleUserActive: async (id) => (await apiClient.put(`/admin/users/${id}/toggle-active`)).data,
    createCategory: async (data) => (await apiClient.post('/admin/categories', data)).data,
    updateCategory: async (id, data) => (await apiClient.put(`/admin/categories/${id}`, data)).data,
    createProduct: async (data) => (await apiClient.post('/admin/products', data)).data,
    updateProduct: async (id, data) => (await apiClient.put(`/admin/products/${id}`, data)).data,
    updateStock: async (id, stockQuantity) => (await apiClient.put(`/admin/products/${id}/stock`, { stockQuantity })).data,
    updatePrice: async (id, price) => (await apiClient.put(`/admin/products/${id}/price`, { price })).data,
    deleteProduct: async (id) => (await apiClient.delete(`/admin/products/${id}`)).data,
    getSettings: async () => (await apiClient.get('/admin/settings')).data,
    updateSettings: async (settings) => (await apiClient.put('/admin/settings', settings)).data,
    backup: async () => (await apiClient.get('/admin/backup')).data,
};

export default apiClient;
