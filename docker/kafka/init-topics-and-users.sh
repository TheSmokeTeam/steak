#!/usr/bin/env bash
set -euo pipefail

bootstrap_server="${STEAK_BOOTSTRAP_SERVER:-steak-kafka:29092}"
scram_username="${STEAK_SCRAM_USERNAME:-admin}"
scram_password="${STEAK_SCRAM_PASSWORD:-admin}"

topics=(
  "orders:3"
  "payments:2"
  "users:2"
  "notifications:1"
  "audit-log:3"
  "inventory.updates:2"
  "shipping.events:2"
  "analytics.clickstream:4"
  "platform.health:1"
  "customer.feedback:2"
)

echo "Creating Kafka topics through ${bootstrap_server}..."
for entry in "${topics[@]}"; do
  IFS=":" read -r topic_name partitions <<< "${entry}"
  kafka-topics \
    --bootstrap-server "${bootstrap_server}" \
    --create \
    --if-not-exists \
    --topic "${topic_name}" \
    --partitions "${partitions}" \
    --replication-factor 1
done

if [[ "${STEAK_CREATE_SCRAM_USER:-false}" == "true" ]]; then
  echo "Ensuring SCRAM-SHA-512 credentials exist for ${scram_username}..."
  kafka-configs \
    --bootstrap-server "${bootstrap_server}" \
    --alter \
    --add-config "SCRAM-SHA-512=[password=${scram_password}]" \
    --entity-type users \
    --entity-name "${scram_username}"
fi

echo "Kafka topics ready:"
kafka-topics --bootstrap-server "${bootstrap_server}" --list
