using System.Net;
using System.Net.Mail;
using System.Threading.Channels;
using PCStore.Core.Entities;

namespace PCStore.API.Services;

public sealed class OrderStatusEmailQueue(
    IConfiguration configuration,
    ILogger<OrderStatusEmailQueue> logger) : BackgroundService
{
    private readonly Channel<OrderStatusEmailJob> _queue = Channel.CreateBounded<OrderStatusEmailJob>(
        new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    public bool TryQueue(string recipient, Order order, string status)
    {
        var portIsValid = int.TryParse(configuration["Email:SmtpPort"], out var port)
            && port is > 0 and <= 65535;
        if (string.IsNullOrWhiteSpace(configuration["Email:SmtpHost"])
            || !portIsValid
            || !MailAddress.TryCreate(configuration["Email:FromAddress"], out _)
            || !MailAddress.TryCreate(recipient, out _))
        {
            logger.LogWarning(
                "Order {OrderCode} status was saved, but its email notification could not be queued because email settings or recipient are invalid.",
                order.OrderCode);
            return false;
        }

        var username = configuration["Email:SmtpUsername"];
        var password = configuration["Email:SmtpPassword"];
        if (string.IsNullOrWhiteSpace(username) != string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning(
                "Order {OrderCode} status was saved, but its email notification could not be queued because SMTP credentials are incomplete.",
                order.OrderCode);
            return false;
        }

        var job = new OrderStatusEmailJob(
            recipient,
            order.OrderCode,
            order.ReceiverName,
            order.TotalAmount,
            order.ShippingAddress,
            status,
            order.OrderDetails
                .Select(item => $"{item.Product?.Name ?? $"Sản phẩm {item.ProductId}"} x {item.Quantity} - {item.UnitPrice * item.Quantity:N0} đ")
                .ToArray());

        if (_queue.Writer.TryWrite(job))
            return true;

        logger.LogError(
            "Order {OrderCode} status was saved, but the email notification queue is full.",
            order.OrderCode);
        return false;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await SendEmailAsync(job, stoppingToken);
            }
            catch (SmtpException exception)
            {
                logger.LogError(exception, "Could not send status email for order {OrderCode}.", job.OrderCode);
            }
            catch (FormatException exception)
            {
                logger.LogError(exception, "Could not send status email for order {OrderCode} because an email address is invalid.", job.OrderCode);
            }
            catch (InvalidOperationException exception)
            {
                logger.LogError(exception, "Could not send status email for order {OrderCode}.", job.OrderCode);
            }
        }
    }

    private async Task SendEmailAsync(OrderStatusEmailJob job, CancellationToken cancellationToken)
    {
        var sender = new MailAddress(configuration["Email:FromAddress"]!);
        var username = configuration["Email:SmtpUsername"];
        var password = configuration["Email:SmtpPassword"];
        var port = int.Parse(configuration["Email:SmtpPort"]!);

        using var message = new MailMessage
        {
            From = new MailAddress(sender.Address, configuration["Email:FromName"] ?? "PC STORE"),
            Subject = $"Cập nhật đơn hàng {job.OrderCode}: {job.Status}",
            Body = $"""
                Xin chào {job.ReceiverName},

                PC STORE đã cập nhật đơn hàng của bạn.
                Mã đơn hàng: {job.OrderCode}
                Trạng thái: {job.Status}
                Thanh toán: COD (thanh toán khi nhận hàng)
                Giao hàng: Tiêu chuẩn - Miễn phí

                Sản phẩm:
                {string.Join(Environment.NewLine, job.OrderLines)}

                Tổng thanh toán: {job.TotalAmount:N0} đ
                Địa chỉ giao hàng: {job.ShippingAddress}

                Cảm ơn bạn đã mua sắm tại PC STORE.
                """,
            IsBodyHtml = false
        };
        message.To.Add(new MailAddress(job.Recipient));

        using var client = new SmtpClient(configuration["Email:SmtpHost"]!, port)
        {
            EnableSsl = !bool.TryParse(configuration["Email:SmtpEnableSsl"], out var enableSsl) || enableSsl
        };
        if (!string.IsNullOrWhiteSpace(username))
            client.Credentials = new NetworkCredential(username, password);

        await client.SendMailAsync(message, cancellationToken);
    }

    private sealed record OrderStatusEmailJob(
        string Recipient,
        string OrderCode,
        string ReceiverName,
        decimal TotalAmount,
        string ShippingAddress,
        string Status,
        IReadOnlyList<string> OrderLines);
}
