using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Nethereum.Signer;
using Nethereum.Signer.EIP712;
using Nethereum.ABI.EIP712;
using Nethereum.ABI.Encoders;
using Nethereum.Hex.HexConvertors.Extensions;
using Nethereum.Util;

namespace PolyMarket.AutoBet.Clients;

public class PolymarketOrderClient
{
    private readonly HttpClient _http;
    private readonly ILogger<PolymarketOrderClient> _logger;
    private readonly EthECKey? _signer;
    private readonly string _address;
    private readonly int _chainId;

    // Polymarket CTF Exchange contract on Polygon
    private const string CtfExchangeAddress = "0x4bFb41d5B3570DeFd03C39a9A4D8dE6Bd8B8982E";
    private const int PolygonChainId = 137;

    public bool IsConfigured => _signer is not null;

    public PolymarketOrderClient(
        HttpClient http,
        ILogger<PolymarketOrderClient> logger,
        IConfiguration config)
    {
        _http = http;
        _logger = logger;
        _chainId = PolygonChainId;

        var privateKey = config["AutoBet:PrivateKey"];
        if (!string.IsNullOrEmpty(privateKey))
        {
            _signer = new EthECKey(privateKey);
            _address = _signer.GetPublicAddress();
            _logger.LogInformation("Order client initialized, address={Address}", _address);
        }
        else
        {
            _address = "";
            _logger.LogWarning("AutoBet: Private key not configured, orders will be simulated");
        }
    }

    public async Task<OrderResult> PlaceOrderAsync(
        string tokenId,
        string side,
        decimal size,
        decimal price,
        CancellationToken ct = default)
    {
        if (!IsConfigured)
        {
            _logger.LogWarning("SIMULATED ORDER: {Side} {Size} @ {Price} token={TokenId}",
                side, size, price, tokenId);
            return new OrderResult(
                Success: true,
                OrderId: $"SIM-{Guid.NewGuid():N}",
                Error: null,
                Simulated: true);
        }

        try
        {
            var order = CreateOrder(tokenId, side, size, price);
            var signature = SignOrder(order);

            var payload = new
            {
                order = new
                {
                    salt = order.Salt.ToString(),
                    maker = order.Maker,
                    signer = order.Signer,
                    taker = order.Taker,
                    tokenId = order.TokenId,
                    makerAmount = order.MakerAmount.ToString(),
                    takerAmount = order.TakerAmount.ToString(),
                    side = order.Side,
                    expiration = order.Expiration.ToString(),
                    nonce = order.Nonce.ToString(),
                    feeRateBps = order.FeeRateBps.ToString(),
                    signatureType = 0
                },
                owner = _address,
                orderType = side == "BUY" ? "GTC" : "GTC",
                signature
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _http.PostAsync("order", content, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<JsonElement>(responseBody);
                var orderId = result.TryGetProperty("orderID", out var idEl)
                    ? idEl.GetString() ?? ""
                    : "unknown";

                _logger.LogInformation("Order placed: {OrderId} {Side} {Size} @ {Price}",
                    orderId, side, size, price);

                return new OrderResult(true, orderId, null, false);
            }
            else
            {
                _logger.LogError("Order failed: {Status} {Body}",
                    response.StatusCode, responseBody);
                return new OrderResult(false, "", responseBody, false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to place order");
            return new OrderResult(false, "", ex.Message, false);
        }
    }

    private ClobOrder CreateOrder(string tokenId, string side, decimal size, decimal price)
    {
        // CLOB uses USDC (6 decimals) for amounts
        var isBuy = side.Equals("BUY", StringComparison.OrdinalIgnoreCase);

        // makerAmount = what maker gives
        // takerAmount = what maker receives
        // BUY: maker gives USDC, receives tokens
        // SELL: maker gives tokens, receives USDC
        var usdcAmount = (BigInteger)(size * price * 1_000_000m); // 6 decimals USDC
        var tokenAmount = (BigInteger)(size * 1_000_000m);

        return new ClobOrder
        {
            Salt = (BigInteger)Random.Shared.NextInt64(),
            Maker = _address,
            Signer = _address,
            Taker = "0x0000000000000000000000000000000000000000",
            TokenId = tokenId,
            MakerAmount = isBuy ? usdcAmount : tokenAmount,
            TakerAmount = isBuy ? tokenAmount : usdcAmount,
            Side = isBuy ? "BUY" : "SELL",
            Expiration = BigInteger.Zero,
            Nonce = BigInteger.Zero,
            FeeRateBps = 0,
            SignatureType = 0
        };
    }

    private string SignOrder(ClobOrder order)
    {
        // EIP-712 domain separator for Polymarket CTF Exchange
        var typedData = new TypedData<ClobOrderDomain>
        {
            Domain = new ClobOrderDomain
            {
                Name = "Polymarket CTF Exchange",
                Version = "1",
                ChainId = _chainId,
                VerifyingContract = CtfExchangeAddress
            },
            Types = new Dictionary<string, MemberDescription[]>
            {
                ["EIP712Domain"] =
                [
                    new() { Name = "name", Type = "string" },
                    new() { Name = "version", Type = "string" },
                    new() { Name = "chainId", Type = "uint256" },
                    new() { Name = "verifyingContract", Type = "address" }
                ],
                ["Order"] =
                [
                    new() { Name = "salt", Type = "uint256" },
                    new() { Name = "maker", Type = "address" },
                    new() { Name = "signer", Type = "address" },
                    new() { Name = "taker", Type = "address" },
                    new() { Name = "tokenId", Type = "uint256" },
                    new() { Name = "makerAmount", Type = "uint256" },
                    new() { Name = "takerAmount", Type = "uint256" },
                    new() { Name = "expiration", Type = "uint256" },
                    new() { Name = "nonce", Type = "uint256" },
                    new() { Name = "feeRateBps", Type = "uint256" },
                    new() { Name = "side", Type = "uint8" },
                    new() { Name = "signatureType", Type = "uint8" }
                ]
            },
            PrimaryType = "Order",
            Message = new[]
            {
                new MemberValue { TypeName = "uint256", Value = order.Salt },
                new MemberValue { TypeName = "address", Value = order.Maker },
                new MemberValue { TypeName = "address", Value = order.Signer },
                new MemberValue { TypeName = "address", Value = order.Taker },
                new MemberValue { TypeName = "uint256", Value = BigInteger.Parse(order.TokenId) },
                new MemberValue { TypeName = "uint256", Value = order.MakerAmount },
                new MemberValue { TypeName = "uint256", Value = order.TakerAmount },
                new MemberValue { TypeName = "uint256", Value = order.Expiration },
                new MemberValue { TypeName = "uint256", Value = order.Nonce },
                new MemberValue { TypeName = "uint256", Value = new BigInteger(order.FeeRateBps) },
                new MemberValue { TypeName = "uint8", Value = order.Side == "BUY" ? 0 : 1 },
                new MemberValue { TypeName = "uint8", Value = order.SignatureType }
            }
        };

        var signer712 = new Eip712TypedDataSigner();
        var signature = signer712.SignTypedDataV4(typedData, _signer!);

        return signature;
    }
}

public class ClobOrderDomain : IDomain
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public int ChainId { get; set; }
    public string VerifyingContract { get; set; } = "";
}

public class ClobOrder
{
    public BigInteger Salt { get; set; }
    public string Maker { get; set; } = "";
    public string Signer { get; set; } = "";
    public string Taker { get; set; } = "";
    public string TokenId { get; set; } = "";
    public BigInteger MakerAmount { get; set; }
    public BigInteger TakerAmount { get; set; }
    public string Side { get; set; } = "";
    public BigInteger Expiration { get; set; }
    public BigInteger Nonce { get; set; }
    public int FeeRateBps { get; set; }
    public int SignatureType { get; set; }
}

public record OrderResult(
    bool Success,
    string OrderId,
    string? Error,
    bool Simulated);
