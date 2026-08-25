import axios from 'axios';

// Dùng 127.0.0.1 thay cho localhost để tương thích tuyệt đối trên macOS
const API_BASE_URL = 'http://127.0.0.1:5170/api';

const apiClient = axios.create({
  baseURL: API_BASE_URL,
  headers: {
    'Content-Type': 'application/json',
  },
  timeout: 10000,
});

export const authService = {
    // Đăng ký tài khoản
    register: async(userData) => {
        const response = await apiClient.post('/auth/register', userData);
        return response.data;
    },

    // Đăng nhập
    login: async(credentials) => {
        const response = await apiClient.post('/auth/login', credentials);
        return response.data;
    },

    // Đăng xuất
    logout: async() => {
        const response = await apiClient.post('/auth/logout');
        return response.data;
    },

    // Lấy hồ sơ người dùng
    getProfile: async(userId) => {
        const response = await apiClient.get(`/users/profile/${userId}`);
        return response.data;
    },

    // Cập nhật hồ sơ & Đổi mật khẩu
    updateProfile: async(userId, data) => {
        const response = await apiClient.put(`/users/profile/${userId}`, data);
        return response.data;
    }
};

export const productService = {
    // Tìm kiếm và lọc linh kiện
    getProducts: async(filters = {}) => {
        const { searchTerm, categoryType, minPrice, maxPrice, sortBy } = filters;
        const params = {};

        if (searchTerm) params.searchTerm = searchTerm;
        if (categoryType && categoryType !== 'ALL') params.categoryType = categoryType;
        if (minPrice !== undefined && minPrice !== '') params.minPrice = minPrice;
        if (maxPrice !== undefined && maxPrice !== '') params.maxPrice = maxPrice;
        if (sortBy) params.sortBy = sortBy;

        const response = await apiClient.get('/products', { params });
        return response.data;
    },

    // Lấy danh mục linh kiện
    getCategories: async() => {
        const response = await apiClient.get('/products/categories');
        return response.data;
    },

    // Lấy chi tiết một sản phẩm
    getProductById: async(id) => {
        const response = await apiClient.get(`/products/${id}`);
        return response.data;
    }
};

export default apiClient;