namespace MicroPlumberd.Migration.Runner.Migrations;

/// <summary>
/// Drops the five hard-tombstoned test Offer streams left on the :5081 store. Their events must not be
/// carried into the fresh destination; the copy engine additionally skips the tombstone markers themselves.
/// </summary>
public sealed class _0001_DropTombstonedTestOffers : Migration
{
    public override string Id => "0001_drop_tombstoned_test_offers";

    public override void Migrate(IMigrationBuilder b) => b.DropStreams(
        "Offer-of-ee959052-0d0e-481e-b45f-c3ce7a6007a7",
        "Offer-of-3d619f92-976e-4201-8510-ce41123d5395",
        "Offer-of-b8eeae43-078c-47f5-b262-84c686fd44fd",
        "Offer-of-f098b22e-e55f-4f46-92bf-1869e1cb7221",
        "Offer-of-d5b2d31f-5a5c-4561-9074-7b97ef50f64a");
}
