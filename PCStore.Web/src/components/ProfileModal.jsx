import React, { useState, useEffect } from 'react';
import { profileService } from '../services/api';

export default function ProfileModal({ isOpen, onClose, currentUser, onUserUpdated }) {
  const [activeTab, setActiveTab] = useState('info'); // 'info' | 'password'
  const [loading, setLoading] = useState(false);
  const [fetching, setFetching] = useState(false);
  const [alert, setAlert] = useState({ type: '', message: '' });

  const [profileForm, setProfileForm] = useState({
    fullName: '',
    email: '',
    phoneNumber: '',
    address: '',
    role: '',
    createdAt: ''
  });

  const [passwordForm, setPasswordForm] = useState({
    currentPassword: '',
    newPassword: '',
    confirmPassword: ''
  });

  useEffect(() => {
    if (isOpen && currentUser?.id) {
      setAlert({ type: '', message: '' });
      setPasswordForm({ currentPassword: '', newPassword: '', confirmPassword: '' });
      loadProfileData();
    }
  }, [isOpen, currentUser]);

  const loadProfileData = async () => {
    try {
      setFetching(true);
      const data = await profileService.getProfile(currentUser.id);
      setProfileForm({
        fullName: data.fullName || '',
        email: data.email || '',
        phoneNumber: data.phoneNumber || '',
        address: data.address || '',
        role: data.role || 'Customer',
        createdAt: data.createdAt ? new Date(data.createdAt).toLocaleDateString('vi-VN') : ''
      });
    } catch (err) {
      setAlert({
        type: 'error',
        message: err.response?.data?.message || 'Không thể tải thông tin hồ sơ.'
      });
    } finally {
      setFetching(false);
    }
  };

  const handleUpdateInfo = async (e) => {
    e.preventDefault();
    if (!profileForm.fullName.trim()) {
      setAlert({ type: 'error', message: 'Họ và tên không được để trống.' });
      return;
    }

    try {
      setLoading(true);
      setAlert({ type: '', message: '' });
      const res = await profileService.updateProfile(currentUser.id, {
        fullName: profileForm.fullName,
        phoneNumber: profileForm.phoneNumber,
        address: profileForm.address
      });

      setAlert({ type: 'success', message: res.message || 'Cập nhật thông tin thành công!' });
      
      if (onUserUpdated && res.user) {
        onUserUpdated(res.user);
      }
    } catch (err) {
      setAlert({
        type: 'error',
        message: err.response?.data?.message || 'Lỗi khi cập nhật thông tin.'
      });
    } finally {
      setLoading(false);
    }
  };

  const handleChangePassword = async (e) => {
    e.preventDefault();
    if (!passwordForm.currentPassword || !passwordForm.newPassword) {
      setAlert({ type: 'error', message: 'Vui lòng điền đầy đủ thông tin.' });
      return;
    }

    if (passwordForm.newPassword.length < 6) {
      setAlert({ type: 'error', message: 'Mật khẩu mới phải có tối thiểu 6 ký tự.' });
      return;
    }

    if (passwordForm.newPassword !== passwordForm.confirmPassword) {
      setAlert({ type: 'error', message: 'Mật khẩu xác nhận không khớp.' });
      return;
    }

    try {
      setLoading(true);
      setAlert({ type: '', message: '' });
      const res = await profileService.changePassword(currentUser.id, {
        currentPassword: passwordForm.currentPassword,
        newPassword: passwordForm.newPassword
      });

      setAlert({ type: 'success', message: res.message || 'Đổi mật khẩu thành công!' });
      setPasswordForm({ currentPassword: '', newPassword: '', confirmPassword: '' });
    } catch (err) {
      setAlert({
        type: 'error',
        message: err.response?.data?.message || 'Đổi mật khẩu thất bại.'
      });
    } finally {
      setLoading(false);
    }
  };

  if (!isOpen) return null;

  return (
    <div style={styles.overlay}>
      <div style={styles.modal}>
        {/* Modal Header */}
        <div style={styles.header}>
          <h2 style={styles.title}>Quản Lý Hồ Sơ Cá Nhân</h2>
          <button style={styles.closeBtn} onClick={onClose}>✕</button>
        </div>

        {/* Navigation Tabs */}
        <div style={styles.tabContainer}>
          <button
            style={activeTab === 'info' ? styles.tabActive : styles.tab}
            onClick={() => { setActiveTab('info'); setAlert({ type: '', message: '' }); }}
          >
            Thông Tin Cá Nhân
          </button>
          <button
            style={activeTab === 'password' ? styles.tabActive : styles.tab}
            onClick={() => { setActiveTab('password'); setAlert({ type: '', message: '' }); }}
          >
            Đổi Mật Khẩu
          </button>
        </div>

        {/* Thông báo Alert */}
        {alert.message && (
          <div style={alert.type === 'success' ? styles.alertSuccess : styles.alertError}>
            {alert.message}
          </div>
        )}

        {fetching ? (
          <div style={styles.loading}>Đang tải dữ liệu hồ sơ...</div>
        ) : (
          <div style={styles.body}>
            {/* TAB THÔNG TIN */}
            {activeTab === 'info' && (
              <form onSubmit={handleUpdateInfo}>
                <div style={styles.formGroup}>
                  <label style={styles.label}>Email định danh (Không thể sửa)</label>
                  <input
                    type="text"
                    value={profileForm.email}
                    disabled
                    style={styles.inputDisabled}
                  />
                </div>

                <div style={styles.formGroup}>
                  <label style={styles.label}>Họ và Tên (*)</label>
                  <input
                    type="text"
                    value={profileForm.fullName}
                    onChange={(e) => setProfileForm({ ...profileForm, fullName: e.target.value })}
                    required
                    style={styles.input}
                  />
                </div>

                <div style={styles.formGroup}>
                  <label style={styles.label}>Số Điện Thoại</label>
                  <input
                    type="tel"
                    value={profileForm.phoneNumber}
                    onChange={(e) => setProfileForm({ ...profileForm, phoneNumber: e.target.value })}
                    placeholder="VD: 0901234567"
                    style={styles.input}
                  />
                </div>

                <div style={styles.formGroup}>
                  <label style={styles.label}>Địa Chỉ Nhận Hàng</label>
                  <input
                    type="text"
                    value={profileForm.address}
                    onChange={(e) => setProfileForm({ ...profileForm, address: e.target.value })}
                    placeholder="Số nhà, tên đường, phường/xã, quận/huyện..."
                    style={styles.input}
                  />
                </div>

                <div style={styles.row}>
                  <div style={{ flex: 1, marginRight: '10px' }}>
                    <label style={styles.label}>Vai Trò</label>
                    <input type="text" value={profileForm.role} disabled style={styles.inputDisabled} />
                  </div>
                  <div style={{ flex: 1 }}>
                    <label style={styles.label}>Ngày Đăng Ký</label>
                    <input type="text" value={profileForm.createdAt} disabled style={styles.inputDisabled} />
                  </div>
                </div>

                <button type="submit" disabled={loading} style={styles.submitBtn}>
                  {loading ? 'Đang lưu...' : 'Lưu Thay Đổi'}
                </button>
              </form>
            )}

            {/* TAB ĐỔI MẬT KHẨU */}
            {activeTab === 'password' && (
              <form onSubmit={handleChangePassword}>
                <div style={styles.formGroup}>
                  <label style={styles.label}>Mật Khẩu Hiện Tại (*)</label>
                  <input
                    type="password"
                    value={passwordForm.currentPassword}
                    onChange={(e) => setPasswordForm({ ...passwordForm, currentPassword: e.target.value })}
                    required
                    placeholder="Nhập mật khẩu đang dùng"
                    style={styles.input}
                  />
                </div>

                <div style={styles.formGroup}>
                  <label style={styles.label}>Mật Khẩu Mới (*)</label>
                  <input
                    type="password"
                    value={passwordForm.newPassword}
                    onChange={(e) => setPasswordForm({ ...passwordForm, newPassword: e.target.value })}
                    required
                    placeholder="Tối thiểu 6 ký tự"
                    style={styles.input}
                  />
                </div>

                <div style={styles.formGroup}>
                  <label style={styles.label}>Nhập Lại Mật Khẩu Mới (*)</label>
                  <input
                    type="password"
                    value={passwordForm.confirmPassword}
                    onChange={(e) => setPasswordForm({ ...passwordForm, confirmPassword: e.target.value })}
                    required
                    placeholder="Xác nhận mật khẩu mới"
                    style={styles.input}
                  />
                </div>

                <button type="submit" disabled={loading} style={styles.submitBtn}>
                  {loading ? 'Đang cập nhật...' : 'Xác Nhận Đổi Mật Khẩu'}
                </button>
              </form>
            )}
          </div>
        )}
      </div>
    </div>
  );
}

const styles = {
  overlay: {
    position: 'fixed',
    top: 0,
    left: 0,
    right: 0,
    bottom: 0,
    backgroundColor: 'rgba(0, 0, 0, 0.75)',
    display: 'flex',
    justifyContent: 'center',
    alignItems: 'center',
    zIndex: 1000,
    backdropFilter: 'blur(4px)',
  },
  modal: {
    backgroundColor: '#1e293b',
    borderRadius: '12px',
    width: '90%',
    maxWidth: '520px',
    padding: '24px',
    color: '#f8fafc',
    boxShadow: '0 20px 25px -5px rgba(0, 0, 0, 0.5), 0 8px 10px -6px rgba(0, 0, 0, 0.5)',
  },
  header: {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    marginBottom: '16px',
  },
  title: {
    fontSize: '1.25rem',
    fontWeight: 'bold',
    color: '#38bdf8',
    margin: 0,
  },
  closeBtn: {
    background: 'none',
    border: 'none',
    color: '#94a3b8',
    fontSize: '1.25rem',
    cursor: 'pointer',
    padding: '4px 8px',
  },
  tabContainer: {
    display: 'flex',
    borderBottom: '1px solid #334155',
    marginBottom: '16px',
  },
  tab: {
    flex: 1,
    padding: '10px 0',
    background: 'none',
    border: 'none',
    color: '#94a3b8',
    fontSize: '0.95rem',
    fontWeight: '500',
    cursor: 'pointer',
    borderBottom: '2px solid transparent',
  },
  tabActive: {
    flex: 1,
    padding: '10px 0',
    background: 'none',
    border: 'none',
    color: '#38bdf8',
    fontSize: '0.95rem',
    fontWeight: '600',
    cursor: 'pointer',
    borderBottom: '2px solid #38bdf8',
  },
  body: {
    marginTop: '8px',
  },
  formGroup: {
    marginBottom: '14px',
  },
  row: {
    display: 'flex',
    marginBottom: '14px',
  },
  label: {
    display: 'block',
    fontSize: '0.85rem',
    color: '#cbd5e1',
    marginBottom: '6px',
  },
  input: {
    width: '100%',
    padding: '10px 12px',
    borderRadius: '6px',
    border: '1px solid #475569',
    backgroundColor: '#0f172a',
    color: '#ffffff',
    fontSize: '0.9rem',
    outline: 'none',
    boxSizing: 'border-box',
  },
  inputDisabled: {
    width: '100%',
    padding: '10px 12px',
    borderRadius: '6px',
    border: '1px solid #334155',
    backgroundColor: '#1e293b',
    color: '#64748b',
    fontSize: '0.9rem',
    boxSizing: 'border-box',
  },
  submitBtn: {
    width: '100%',
    padding: '12px',
    backgroundColor: '#0284c7',
    color: '#ffffff',
    border: 'none',
    borderRadius: '6px',
    fontWeight: 'bold',
    fontSize: '0.95rem',
    cursor: 'pointer',
    marginTop: '10px',
    transition: 'background-color 0.2s',
  },
  alertSuccess: {
    backgroundColor: 'rgba(16, 185, 129, 0.2)',
    color: '#34d399',
    padding: '10px 12px',
    borderRadius: '6px',
    marginBottom: '12px',
    fontSize: '0.85rem',
    border: '1px solid rgba(16, 185, 129, 0.3)',
  },
  alertError: {
    backgroundColor: 'rgba(239, 68, 68, 0.2)',
    color: '#f87171',
    padding: '10px 12px',
    borderRadius: '6px',
    marginBottom: '12px',
    fontSize: '0.85rem',
    border: '1px solid rgba(239, 68, 68, 0.3)',
  },
  loading: {
    textAlign: 'center',
    padding: '30px 0',
    color: '#94a3b8',
    fontSize: '0.9rem',
  }
};