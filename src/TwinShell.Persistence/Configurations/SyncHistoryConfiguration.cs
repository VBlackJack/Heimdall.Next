/*
 * Copyright 2025 Julien Bombled
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TwinShell.Persistence.Entities;

namespace TwinShell.Persistence.Configurations;

/// <summary>
/// EF Core configuration for SyncHistoryEntity
/// </summary>
public class SyncHistoryConfiguration : IEntityTypeConfiguration<SyncHistoryEntity>
{
    public void Configure(EntityTypeBuilder<SyncHistoryEntity> builder)
    {
        builder.ToTable("SyncHistories");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.OperationType)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(e => e.Message)
            .HasMaxLength(1000);

        builder.Property(e => e.ErrorDetails)
            .HasMaxLength(4000);

        builder.Property(e => e.RemoteUrl)
            .HasMaxLength(500);

        builder.Property(e => e.Branch)
            .HasMaxLength(100);

        builder.Property(e => e.StartedAt)
            .IsRequired();

        builder.Property(e => e.CompletedAt)
            .IsRequired();

        // Indexes for faster queries
        builder.HasIndex(e => e.StartedAt);
        builder.HasIndex(e => e.OperationType);
        builder.HasIndex(e => e.Success);
        builder.HasIndex(e => new { e.StartedAt, e.OperationType });
    }
}
