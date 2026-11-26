using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;

/// <summary>
/// Real-money store (Unity IAP v5): the VIP pass and the premium cars, all non-consumable.
/// Purchases are granted into PlayerProfile when the store reports them pending, then confirmed;
/// owned items are re-granted from FetchPurchases / RestoreTransactions after a reinstall.
/// In the Editor Unity IAP runs its fake store, so the flow can be tried without Google Play.
/// </summary>
public static class GameStore
{
    public const string VipProduct = "vip_pass";

    // Store product id -> catalogue car id.
    private static readonly Dictionary<string, string> CarProducts = new Dictionary<string, string>
    {
        { "car_stinger", "stinger" },
        { "car_apex", "apex" }
    };

    // Shown until the store returns localized prices.
    private static readonly Dictionary<string, string> FallbackPrices = new Dictionary<string, string>
    {
        { VipProduct, "$3.99" },
        { "car_stinger", "$2.99" },
        { "car_apex", "$4.99" }
    };

    private static StoreController _store;
    private static bool _started;

    public static bool Ready { get; private set; }
    public static bool Busy { get; private set; }

    /// <summary>Last message for the UI (purchase done, failed, store unavailable).</summary>
    public static string Message { get; private set; }
    public static float MessageAt { get; private set; } = -10f;

    public static async void Init()
    {
        if (_started) return;
        _started = true;
        try
        {
            _store = UnityIAPServices.StoreController();

            // Every event is wired before Connect: purchases left pending last session arrive at once.
            _store.OnPurchasePending += OnPurchasePending;
            _store.OnPurchaseConfirmed += OnPurchaseConfirmed;
            _store.OnPurchaseFailed += failed =>
            {
                Busy = false;
                Say(failed.FailureReason == PurchaseFailureReason.UserCancelled ? "Purchase cancelled." : "Purchase failed: " + failed.FailureReason);
            };
            _store.OnPurchaseDeferred += deferred =>
            {
                Busy = false;
                Say("Purchase is waiting for approval. It will unlock once the payment goes through.");
            };
            _store.OnStoreConnected += OnStoreConnected;
            _store.OnStoreDisconnected += failure =>
            {
                Ready = false;
                Debug.LogWarning("Store disconnected: " + failure.Message);
            };
            _store.OnProductsFetched += products => Ready = true;
            _store.OnProductsFetchFailed += failure => Debug.LogWarning("Store products failed: " + failure.FailureReason);
            _store.OnPurchasesFetched += OnPurchasesFetched;
            _store.OnPurchasesFetchFailed += failure => Debug.LogWarning("Store purchases failed: " + failure.Message);

            await _store.Connect();
        }
        catch (System.Exception e)
        {
            // No store on this device (or no Play services): the game works without it.
            Debug.LogWarning("Store unavailable: " + e.Message);
        }
    }

    private static void OnStoreConnected()
    {
        var products = new List<ProductDefinition> { new ProductDefinition(VipProduct, ProductType.NonConsumable) };
        foreach (var id in CarProducts.Keys) products.Add(new ProductDefinition(id, ProductType.NonConsumable));
        _store.FetchProducts(products);
        _store.FetchPurchases();
    }

    // ------------------------------------------------------------------ queries

    public static string ProductForCar(int carIndex)
    {
        var carId = CarCatalog.Get(carIndex).Id;
        foreach (var pair in CarProducts)
            if (pair.Value == carId) return pair.Key;
        return null;
    }

    public static string Price(string productId)
    {
        var product = Ready && productId != null ? _store.GetProductById(productId) : null;
        var price = product?.metadata?.localizedPriceString;
        if (!string.IsNullOrEmpty(price)) return price;
        return productId != null && FallbackPrices.TryGetValue(productId, out var fallback) ? fallback : "";
    }

    // ------------------------------------------------------------------ actions

    public static void Buy(string productId)
    {
        if (productId == null || Busy) return;
        var product = Ready ? _store.GetProductById(productId) : null;
        if (product == null)
        {
            Say("Store not available right now. Check your connection and Google Play.");
            return;
        }

        Busy = true;
        _store.PurchaseProduct(product);
    }

    public static void Restore()
    {
        if (_store == null || !Ready)
        {
            Say("Store not available right now.");
            return;
        }

        Busy = true;
        _store.RestoreTransactions((success, error) =>
        {
            Busy = false;
            Say(success ? "Purchases restored." : "Restore failed: " + error);
        });
    }

    // ------------------------------------------------------------------ store events

    private static void OnPurchasePending(PendingOrder order)
    {
        // Grant first (idempotent: a re-delivered order grants nothing new), then confirm.
        foreach (var item in order.CartOrdered.Items())
            Grant(item.Product.definition.id);
        _store.ConfirmPurchase(order);
    }

    private static void OnPurchaseConfirmed(Order order)
    {
        Busy = false;
        if (order is FailedOrder failed)
        {
            Debug.LogWarning("Purchase confirmation failed: " + failed.FailureReason + " " + failed.Details);
            return;
        }

        foreach (var item in order.CartOrdered.Items())
            Say(NameOf(item.Product.definition.id) + " unlocked. Thank you!");
    }

    private static void OnPurchasesFetched(Orders orders)
    {
        // Owned non-consumables come back here after a reinstall or on a new phone.
        foreach (var order in orders.ConfirmedOrders)
            foreach (var item in order.CartOrdered.Items())
                Grant(item.Product.definition.id);
    }

    /// <summary>Unlocks a product in the profile. Public so the editor tests can exercise it.</summary>
    public static void Grant(string productId)
    {
        if (productId == VipProduct)
        {
            PlayerProfile.GrantVip();
            return;
        }

        if (CarProducts.TryGetValue(productId, out var carId))
            PlayerProfile.GrantCar(CarCatalog.IndexOf(carId));
    }

    private static string NameOf(string productId) =>
        productId == VipProduct ? "VIP pass" : CarProducts.TryGetValue(productId, out var carId) ? CarCatalog.Get(CarCatalog.IndexOf(carId)).Name : productId;

    private static void Say(string message)
    {
        Message = message;
        MessageAt = Time.unscaledTime;
    }
}
