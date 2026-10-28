import { useEffect, useRef, useState } from 'react';
import { Bot, Loader2, MessageCircle, Send, Sparkles, X } from 'lucide-react';
import { aiService, getApiErrorMessage, supportService } from '../services/api';

const welcomeMessage = {
  role: 'assistant',
  text: 'Xin chào! Tôi là trợ lý AI của PCSTORE. Bạn cần tư vấn linh kiện hay sản phẩm nào?',
};

export default function AIChatBox() {
  const [isOpen, setIsOpen] = useState(false);
  const [messages, setMessages] = useState([welcomeMessage]);
  const [input, setInput] = useState('');
  const [isSending, setIsSending] = useState(false);
  const [isSubmittingTicket, setIsSubmittingTicket] = useState(false);
  const [ticketSubject, setTicketSubject] = useState('');
  const [ticketDescription, setTicketDescription] = useState('');
  const [ticketMessage, setTicketMessage] = useState('');
  const messagesEndRef = useRef(null);
  const inputRef = useRef(null);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages, isOpen]);

  useEffect(() => {
    if (isOpen) inputRef.current?.focus();
  }, [isOpen]);

  const sendMessage = async event => {
    event.preventDefault();
    const message = input.trim();
    if (!message || isSending) return;

    setInput('');
    setMessages(current => [...current, { role: 'user', text: message }]);
    setIsSending(true);
    try {
      const result = await aiService.consult(message);
      setMessages(current => [
        ...current,
        { role: 'assistant', text: result.response || 'AI chưa có câu trả lời cho yêu cầu này.' },
      ]);
    } catch (error) {
      setMessages(current => [
        ...current,
        { role: 'error', text: getApiErrorMessage(error, 'Chưa thể kết nối với trợ lý AI. Vui lòng thử lại.') },
      ]);
    } finally {
      setIsSending(false);
      inputRef.current?.focus();
    }
  };

  const submitTicket = async event => {
    event.preventDefault();
    setIsSubmittingTicket(true);
    setTicketMessage('');
    try {
      const ticket = await supportService.createTicket({
        subject: ticketSubject.trim(),
        description: ticketDescription.trim(),
      });
      setTicketMessage(`Đã gửi yêu cầu hỗ trợ #${ticket.id}. Nhân viên sẽ tiếp nhận sớm.`);
      setTicketSubject('');
      setTicketDescription('');
    } catch (error) {
      setTicketMessage(getApiErrorMessage(error, 'Không thể gửi yêu cầu hỗ trợ.'));
    } finally {
      setIsSubmittingTicket(false);
    }
  };

  return (
    <div className="fixed bottom-5 right-5 z-40 flex flex-col items-end gap-3 sm:bottom-6 sm:right-6">
      {isOpen && (
        <section
          aria-label="Chatbox AI PCSTORE"
          className="flex h-[min(72vh,560px)] max-h-[calc(100vh-110px)] w-[min(calc(100vw-2rem),380px)] flex-col overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-2xl"
        >
          <header className="flex items-center justify-between bg-gradient-to-r from-blue-700 to-sky-500 px-4 py-3 text-white">
            <div className="flex items-center gap-3">
              <span className="flex h-9 w-9 items-center justify-center rounded-xl bg-white/15">
                <Sparkles className="h-5 w-5" />
              </span>
              <div>
                <h2 className="text-sm font-bold">Chatbox AI PCSTORE</h2>
                <p className="text-[10px] text-blue-100">Tư vấn linh kiện và sản phẩm</p>
              </div>
            </div>
            <button
              type="button"
              onClick={() => setIsOpen(false)}
              aria-label="Đóng chatbox AI"
              className="rounded-lg p-2 transition hover:bg-white/15"
            >
              <X className="h-4 w-4" />
            </button>
          </header>

          <div className="flex-1 space-y-3 overflow-y-auto bg-slate-50 p-3" aria-live="polite">
            {messages.map((message, index) => (
              <div
                key={`${message.role}-${index}`}
                className={`flex items-start gap-2 ${message.role === 'user' ? 'justify-end' : ''}`}
              >
                {message.role !== 'user' && (
                  <span className={`mt-1 flex h-7 w-7 shrink-0 items-center justify-center rounded-full ${
                    message.role === 'error' ? 'bg-rose-100 text-rose-600' : 'bg-blue-100 text-blue-700'
                  }`}>
                    <Bot className="h-4 w-4" />
                  </span>
                )}
                <p className={`max-w-[84%] whitespace-pre-wrap break-words rounded-2xl px-3 py-2 text-xs leading-relaxed ${
                  message.role === 'user'
                    ? 'rounded-br-sm bg-blue-600 text-white'
                    : message.role === 'error'
                      ? 'rounded-bl-sm border border-rose-200 bg-rose-50 text-rose-700'
                      : 'rounded-bl-sm border border-slate-200 bg-white text-slate-700'
                }`}>
                  {message.text}
                </p>
              </div>
            ))}
            {isSending && (
              <div className="flex items-center gap-2 text-xs text-slate-500">
                <span className="flex h-7 w-7 items-center justify-center rounded-full bg-blue-100 text-blue-700">
                  <Bot className="h-4 w-4" />
                </span>
                <span className="flex items-center gap-2 rounded-2xl border border-slate-200 bg-white px-3 py-2">
                  <Loader2 className="h-3.5 w-3.5 animate-spin" /> AI đang trả lời...
                </span>
              </div>
            )}
            <div ref={messagesEndRef} />
          </div>

          {isOpen && (
            <details className="border-t border-slate-200 bg-white px-3 py-2">
              <summary className="cursor-pointer text-[11px] font-semibold text-blue-700">Gửi yêu cầu cho nhân viên hỗ trợ</summary>
              <form onSubmit={submitTicket} className="mt-2 space-y-2">
                <label className="sr-only" htmlFor="support-ticket-subject">Tiêu đề yêu cầu hỗ trợ</label>
                <input
                  id="support-ticket-subject"
                  value={ticketSubject}
                  onChange={event => setTicketSubject(event.target.value)}
                  maxLength={200}
                  required
                  placeholder="Tiêu đề"
                  className="w-full rounded-lg border border-slate-200 px-2 py-1.5 text-xs text-slate-800"
                />
                <label className="sr-only" htmlFor="support-ticket-description">Nội dung yêu cầu hỗ trợ</label>
                <textarea
                  id="support-ticket-description"
                  value={ticketDescription}
                  onChange={event => setTicketDescription(event.target.value)}
                  maxLength={2000}
                  required
                  rows={3}
                  placeholder="Mô tả vấn đề cần hỗ trợ"
                  className="w-full rounded-lg border border-slate-200 px-2 py-1.5 text-xs text-slate-800"
                />
                {ticketMessage && <p role="status" className="text-[10px] text-slate-600">{ticketMessage}</p>}
                <button
                  type="submit"
                  disabled={isSubmittingTicket || !ticketSubject.trim() || !ticketDescription.trim()}
                  className="rounded-lg bg-slate-800 px-3 py-1.5 text-[10px] font-bold text-white disabled:opacity-50"
                >
                  {isSubmittingTicket ? 'Đang gửi...' : 'Gửi yêu cầu'}
                </button>
              </form>
            </details>
          )}
          <form onSubmit={sendMessage} className="border-t border-slate-200 bg-white p-3">
            <label htmlFor="pcstore-ai-chat-input" className="sr-only">Nhập tin nhắn cho trợ lý AI</label>
            <div className="flex items-end gap-2">
              <textarea
                id="pcstore-ai-chat-input"
                ref={inputRef}
                value={input}
                onChange={event => setInput(event.target.value)}
                onKeyDown={event => {
                  if (event.key === 'Enter' && !event.shiftKey) {
                    event.preventDefault();
                    event.currentTarget.form?.requestSubmit();
                  }
                }}
                maxLength={4000}
                rows={1}
                placeholder="Nhập câu hỏi tư vấn..."
                className="max-h-28 min-h-10 flex-1 resize-y rounded-xl border border-slate-200 px-3 py-2 text-xs text-slate-800 outline-none transition focus:border-blue-400 focus:ring-2 focus:ring-blue-100"
              />
              <button
                type="submit"
                disabled={!input.trim() || isSending}
                aria-label="Gửi tin nhắn"
                className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-blue-600 text-white transition hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50"
              >
                {isSending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Send className="h-4 w-4" />}
              </button>
            </div>
            <p className="mt-1.5 text-right text-[9px] text-slate-400">Enter để gửi · Shift + Enter xuống dòng</p>
          </form>
        </section>
      )}

      <button
        type="button"
        onClick={() => setIsOpen(open => !open)}
        aria-label={isOpen ? 'Đóng chatbox AI PCSTORE' : 'Mở chatbox AI PCSTORE'}
        aria-expanded={isOpen}
        className="group flex items-center gap-2 rounded-full bg-gradient-to-r from-blue-700 to-sky-500 px-4 py-3 text-white shadow-xl ring-4 ring-white/80 transition hover:scale-[1.03] hover:shadow-2xl focus:outline-none focus:ring-4 focus:ring-sky-200"
      >
        {isOpen ? <X className="h-5 w-5" /> : <MessageCircle className="h-5 w-5 group-hover:animate-pulse" />}
        <span className="text-xs font-black tracking-wide">CHAT BOX AI</span>
      </button>
    </div>
  );
}
