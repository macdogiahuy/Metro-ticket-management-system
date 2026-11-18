using Metro.Domain.Entities.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Metro.Infrastructure.Persistence.Configurations;

public class OperatorConfiguration : IEntityTypeConfiguration<Operator>
{
    public void Configure(EntityTypeBuilder<Operator> builder)
    {
        builder.ToTable("operators", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.Code).HasColumnName("code").HasMaxLength(40).IsRequired();
        builder.Property(e => e.Name).HasColumnName("name").IsRequired();
        builder.Property(e => e.Timezone).HasColumnName("timezone").HasDefaultValue("Asia/Ho_Chi_Minh").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasDefaultValue("ACTIVE").IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("clock_timestamp()").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("clock_timestamp()").IsRequired();

        builder.HasIndex(e => e.Code).IsUnique();
    }
}

public class BankConfiguration : IEntityTypeConfiguration<Bank>
{
    public void Configure(EntityTypeBuilder<Bank> builder)
    {
        builder.ToTable("banks", "metro");
        builder.HasKey(e => e.Code);
        builder.Property(e => e.Code).HasColumnName("code").HasMaxLength(40);
        builder.Property(e => e.Name).HasColumnName("name").IsRequired();
        builder.Property(e => e.IsDemo).HasColumnName("is_demo").HasDefaultValue(true).IsRequired();
    }
}

public class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("user_profiles", "metro");
        builder.HasKey(e => e.UserId);
        builder.Property(e => e.UserId).HasColumnName("user_id");
        builder.Property(e => e.FullName).HasColumnName("full_name").IsRequired();
        builder.Property(e => e.NormalizedFullName).HasColumnName("normalized_full_name").IsRequired();
        builder.Property(e => e.BirthDate).HasColumnName("birth_date").IsRequired();
        builder.Property(e => e.CitizenIdCiphertext).HasColumnName("citizen_id_ciphertext");
        builder.Property(e => e.CitizenIdLookupHash).HasColumnName("citizen_id_lookup_hash").HasMaxLength(64);
        builder.Property(e => e.CitizenIdLast4).HasColumnName("citizen_id_last4").HasMaxLength(4);
        builder.Property(e => e.CitizenIdVerifiedAt).HasColumnName("citizen_id_verified_at");
        builder.Property(e => e.BankCode).HasColumnName("bank_code").HasMaxLength(40);
        builder.Property(e => e.BankAccountCiphertext).HasColumnName("bank_account_ciphertext");
        builder.Property(e => e.BankAccountLast4).HasColumnName("bank_account_last4").HasMaxLength(4);
        builder.Property(e => e.BankHolderName).HasColumnName("bank_holder_name");
        builder.Property(e => e.Status).HasColumnName("status").HasDefaultValue("PENDING").IsRequired();
        builder.Property(e => e.Locale).HasColumnName("locale").HasMaxLength(20).HasDefaultValue("vi-VN").IsRequired();
        builder.Property(e => e.EmailNotifications).HasColumnName("email_notifications").HasDefaultValue(true).IsRequired();
        builder.Property(e => e.RegisteredAt).HasColumnName("registered_at").HasDefaultValueSql("clock_timestamp()").IsRequired();
        builder.Property(e => e.NextEligibilityReviewAt).HasColumnName("next_eligibility_review_at");
        builder.Property(e => e.EligibilityReviewNumber).HasColumnName("eligibility_review_number").HasDefaultValue(1).IsRequired();
        builder.Property(e => e.TermsAcceptedAt).HasColumnName("terms_accepted_at");
        builder.Property(e => e.AnonymizedAt).HasColumnName("anonymized_at");
        builder.Property(e => e.AnonymizationReason).HasColumnName("anonymization_reason");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("clock_timestamp()").IsRequired();
        builder.Property(e => e.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("clock_timestamp()").IsRequired();

        builder.HasOne(e => e.Bank)
            .WithMany(b => b.UserProfiles)
            .HasForeignKey(e => e.BankCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(e => e.CitizenIdLookupHash).IsUnique();
    }
}

public class AuthSessionConfiguration : IEntityTypeConfiguration<AuthSession>
{
    public void Configure(EntityTypeBuilder<AuthSession> builder)
    {
        builder.ToTable("auth_sessions", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(e => e.RefreshTokenHash).HasColumnName("refresh_token_hash").HasMaxLength(64).IsRequired();
        builder.Property(e => e.IssuedAt).HasColumnName("issued_at").HasDefaultValueSql("clock_timestamp()").IsRequired();
        builder.Property(e => e.LastActiveAt).HasColumnName("last_active_at");
        builder.Property(e => e.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(e => e.RevokedAt).HasColumnName("revoked_at");
        builder.Property(e => e.RevocationReason).HasColumnName("revocation_reason");
        builder.Property(e => e.DeviceSummary).HasColumnName("device_summary");

        builder.HasIndex(e => e.RefreshTokenHash).IsUnique();
    }
}

public class EmailVerificationChallengeConfiguration : IEntityTypeConfiguration<EmailVerificationChallenge>
{
    public void Configure(EntityTypeBuilder<EmailVerificationChallenge> builder)
    {
        builder.ToTable("email_verification_challenges", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(e => e.Purpose).HasColumnName("purpose").IsRequired();
        builder.Property(e => e.CodeHash).HasColumnName("code_hash").HasMaxLength(64).IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("clock_timestamp()").IsRequired();
        builder.Property(e => e.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(e => e.FailedAttempts).HasColumnName("failed_attempts").HasDefaultValue((short)0).IsRequired();
        builder.Property(e => e.ConsumedAt).HasColumnName("consumed_at");
        builder.Property(e => e.SupersededAt).HasColumnName("superseded_at");
        builder.Property(e => e.DeliveryStatus).HasColumnName("delivery_status").HasDefaultValue("PENDING").IsRequired();
    }
}

public class OperatorAccountConfiguration : IEntityTypeConfiguration<OperatorAccount>
{
    public void Configure(EntityTypeBuilder<OperatorAccount> builder)
    {
        builder.ToTable("operator_accounts", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(e => e.AccountType).HasColumnName("account_type").HasDefaultValue("PASSENGER").IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasDefaultValue("ACTIVE").IsRequired();
        builder.Property(e => e.MfaRequired).HasColumnName("mfa_required").HasDefaultValue(false).IsRequired();

        builder.HasAlternateKey(e => new { e.OperatorId, e.Id });
        builder.HasIndex(e => new { e.OperatorId, e.UserId }).IsUnique();

        builder.HasOne(e => e.Operator)
            .WithMany(o => o.Accounts)
            .HasForeignKey(e => e.OperatorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class BusinessRoleConfiguration : IEntityTypeConfiguration<BusinessRole>
{
    public void Configure(EntityTypeBuilder<BusinessRole> builder)
    {
        builder.ToTable("business_roles", "metro");
        builder.HasKey(e => e.Code);
        builder.Property(e => e.Code).HasColumnName("code").HasMaxLength(60);
        builder.Property(e => e.Name).HasColumnName("name").IsRequired();
        builder.Property(e => e.ActorCode).HasColumnName("actor_code").HasMaxLength(20);
        builder.Property(e => e.IsPrivileged).HasColumnName("is_privileged").HasDefaultValue(false).IsRequired();
    }
}

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("permissions", "metro");
        builder.HasKey(e => e.Code);
        builder.Property(e => e.Code).HasColumnName("code").HasMaxLength(80);
        builder.Property(e => e.Description).HasColumnName("description").IsRequired();
    }
}

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("role_permissions", "metro");
        builder.HasKey(e => new { e.RoleCode, e.PermissionCode, e.Version });
        builder.Property(e => e.RoleCode).HasColumnName("role_code").HasMaxLength(60);
        builder.Property(e => e.PermissionCode).HasColumnName("permission_code").HasMaxLength(80);
        builder.Property(e => e.Version).HasColumnName("version").HasDefaultValue(1);

        builder.HasOne(e => e.Role)
            .WithMany(r => r.RolePermissions)
            .HasForeignKey(e => e.RoleCode)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Permission)
            .WithMany(p => p.RolePermissions)
            .HasForeignKey(e => e.PermissionCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class AccountRoleAssignmentConfiguration : IEntityTypeConfiguration<AccountRoleAssignment>
{
    public void Configure(EntityTypeBuilder<AccountRoleAssignment> builder)
    {
        builder.ToTable("account_role_assignments", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id").IsRequired();
        builder.Property(e => e.AccountId).HasColumnName("account_id").IsRequired();
        builder.Property(e => e.RoleCode).HasColumnName("role_code").HasMaxLength(60).IsRequired();
        builder.Property(e => e.ValidFrom).HasColumnName("valid_from").HasDefaultValueSql("clock_timestamp()").IsRequired();
        builder.Property(e => e.ValidTo).HasColumnName("valid_to");
        builder.Property(e => e.GrantedByUserId).HasColumnName("granted_by_user_id");
        builder.Property(e => e.Reason).HasColumnName("reason").IsRequired();
        builder.Property(e => e.RevokedAt).HasColumnName("revoked_at");
        builder.Property(e => e.RevokedByUserId).HasColumnName("revoked_by_user_id");

        builder.HasAlternateKey(e => new { e.OperatorId, e.Id });

        builder.HasOne(e => e.Operator)
            .WithMany()
            .HasForeignKey(e => e.OperatorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.Account)
            .WithMany(a => a.RoleAssignments)
            .HasForeignKey(e => new { e.OperatorId, e.AccountId })
            .HasPrincipalKey(a => new { a.OperatorId, a.Id })
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(e => e.BusinessRole)
            .WithMany(r => r.AccountRoleAssignments)
            .HasForeignKey(e => e.RoleCode)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs", "metro");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.OperatorId).HasColumnName("operator_id");
        builder.Property(e => e.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(e => e.DatabaseActor).HasColumnName("database_actor").HasDefaultValueSql("current_user").IsRequired();
        builder.Property(e => e.Action).HasColumnName("action").IsRequired();
        builder.Property(e => e.EntityType).HasColumnName("entity_type").IsRequired();
        builder.Property(e => e.EntityId).HasColumnName("entity_id").IsRequired();
        builder.Property(e => e.ChangedFields).HasColumnName("changed_fields").HasDefaultValueSql("'{}'").IsRequired();
        builder.Property(e => e.BeforeHash).HasColumnName("before_hash").HasMaxLength(64);
        builder.Property(e => e.AfterHash).HasColumnName("after_hash").HasMaxLength(64);
        builder.Property(e => e.Reason).HasColumnName("reason");
        builder.Property(e => e.Outcome).HasColumnName("outcome").HasDefaultValue("SUCCEEDED").IsRequired();
        builder.Property(e => e.OccurredAt).HasColumnName("occurred_at").HasDefaultValueSql("clock_timestamp()").IsRequired();
        builder.Property(e => e.CorrelationId).HasColumnName("correlation_id").HasDefaultValueSql("gen_random_uuid()").IsRequired();

        builder.HasOne(e => e.Operator)
            .WithMany()
            .HasForeignKey(e => e.OperatorId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
