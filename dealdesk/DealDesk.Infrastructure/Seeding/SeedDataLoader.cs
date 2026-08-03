using System.Text.Json;
using System.Text.RegularExpressions;
using DealDesk.Application.Abstractions;
using DealDesk.Application.Mapping;
using DealDesk.Domain.Entities;
using DealDesk.Domain.Enums;
using DealDesk.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace DealDesk.Infrastructure.Seeding
{
    /// <summary>
    /// Loads seed-deals.json (the sample data provided with the assessment) into
    /// the deal store at startup. Deals are materialized through Deal.Restore, so
    /// they obey the same invariants and money maths as deals created via the API,
    /// and the reference generator is advanced past the seeded sequence numbers so
    /// new deals continue from there. A malformed seed file fails startup on
    /// purpose: silently running with partial seed data would be worse.
    /// </summary>
    public static class SeedDataLoader
    {
        private static readonly Regex ReferencePattern = new(@"^SF-(\d{4})-(\d+)$", RegexOptions.Compiled);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        public static async Task<int> LoadAsync(
            string path,
            IDealRepository deals,
            IReferenceGenerator references,
            IClock clock,
            ILogger logger,
            CancellationToken cancellationToken = default)
        {
            SeedFile? file;
            await using (var stream = File.OpenRead(path))
            {
                file = await JsonSerializer.DeserializeAsync<SeedFile>(stream, JsonOptions, cancellationToken);
            }

            if (file?.Deals is null || file.Deals.Count == 0)
            {
                logger.LogWarning("Seed file {Path} contains no deals; nothing to load", path);
                return 0;
            }

            var loaded = 0;
            foreach (var seed in file.Deals)
            {
                var deal = Materialize(seed, clock);
                await deals.AddAsync(deal, cancellationToken);

                var match = ReferencePattern.Match(deal.Reference);
                if (match.Success)
                    references.EnsureUsed(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value));

                loaded++;
            }

            logger.LogInformation("Seeded {Count} deals from {Path}", loaded, path);
            return loaded;
        }

        private static Deal Materialize(SeedDeal seed, IClock clock)
        {
            var reference = seed.Reference
                ?? throw new InvalidDataException("Seed deal is missing a reference.");

            if (!Wire.TryParseFundingType(seed.FundingType, out var fundingType))
                throw new InvalidDataException($"Deal {reference}: invalid funding_type '{seed.FundingType}'.");
            if (!Wire.TryParseBuyerSector(seed.BuyerSector, out var buyerSector))
                throw new InvalidDataException($"Deal {reference}: invalid buyer_sector '{seed.BuyerSector}'.");
            if (!Wire.TryParseStatus(seed.Status, out var status))
                throw new InvalidDataException($"Deal {reference}: invalid status '{seed.Status}'.");

            var documents = (seed.Documents ?? new List<SeedDocument>()).Select(d =>
            {
                if (!Wire.TryParseDocumentType(d.DocType, out var docType))
                    throw new InvalidDataException($"Deal {reference}: invalid doc_type '{d.DocType}'.");
                if (string.IsNullOrWhiteSpace(d.Filename))
                    throw new InvalidDataException($"Deal {reference}: document '{d.DocType}' is missing a filename.");
                return new DealDocument(docType, d.Filename);
            }).ToList();

            DealTerms? terms = null;
            if (seed.Terms is not null)
            {
                if (!DealMapper.TryParseIsoDate(seed.Terms.ExpectedSettlementDate, out var settlementDate))
                    throw new InvalidDataException(
                        $"Deal {reference}: invalid expected_settlement_date '{seed.Terms.ExpectedSettlementDate}'.");
                terms = new DealTerms(seed.Terms.AdvanceRateBps, seed.Terms.FacilityFeeRateBps, settlementDate);
            }

            DateOnly? fundedOn = null;
            if (seed.FundedOn is not null)
            {
                if (!DealMapper.TryParseIsoDate(seed.FundedOn, out var parsed))
                    throw new InvalidDataException($"Deal {reference}: invalid funded_on '{seed.FundedOn}'.");
                fundedOn = parsed;
            }

            return Deal.Restore(
                reference,
                seed.ApplicantName ?? throw new InvalidDataException($"Deal {reference}: applicant_name is required."),
                fundingType,
                seed.DealAmountCents ?? throw new InvalidDataException($"Deal {reference}: deal_amount_cents is required."),
                seed.BuyerName ?? throw new InvalidDataException($"Deal {reference}: buyer_name is required."),
                buyerSector,
                status,
                documents,
                terms,
                seed.DeclineReason,
                fundedOn,
                clock.UtcNow);
        }

        private sealed record SeedFile(List<SeedDeal>? Deals);

        private sealed record SeedDeal(
            string? Reference,
            string? ApplicantName,
            string? FundingType,
            long? DealAmountCents,
            string? BuyerName,
            string? BuyerSector,
            string? Status,
            List<SeedDocument>? Documents,
            SeedTerms? Terms,
            string? DeclineReason,
            string? FundedOn);

        private sealed record SeedDocument(string? DocType, string? Filename);

        private sealed record SeedTerms(
            int AdvanceRateBps,
            int FacilityFeeRateBps,
            string? ExpectedSettlementDate);
    }
}
