// ============================================================================
// Module: servicebus.bicep
// Provisions: Service Bus Namespace + booking-confirmed + booking-cancelled
//             topics + a least-privilege Send+Listen auth rule for the API.
//
// NOTE: Topics require Standard or Premium tier. Basic only supports queues.
// ============================================================================

@description('Short application name used for resource naming.')
param appName string

@description('Azure region for all resources.')
param location string

@description('Deployment environment.')
@allowed(['dev', 'prod'])
param environment string

@description('Service Bus namespace SKU. Standard is the minimum for topics.')
@allowed(['Standard', 'Premium'])
param sku string = 'Standard'

// ── Derived names ─────────────────────────────────────────────────────────────
var suffix        = take(uniqueString(resourceGroup().id), 6)
// Service Bus namespace names must be 6–50 chars, globally unique.
var namespaceName = 'sb-${appName}-${environment}-${suffix}'

// Topic names match the convention in ServiceBusEventPublisher.cs:
//   BookingConfirmedEvent → "booking-confirmed"
//   BookingCancelledEvent → "booking-cancelled"
var topicConfirmed  = 'booking-confirmed'
var topicCancelled  = 'booking-cancelled'

// ── Namespace ─────────────────────────────────────────────────────────────────

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: namespaceName
  location: location
  sku: {
    name: sku
    tier: sku
  }
  properties: {
    minimumTlsVersion: '1.2'
    disableLocalAuth: false
    zoneRedundant: false
  }
  tags: {
    environment: environment
    app: appName
  }
}

// ── Topics ────────────────────────────────────────────────────────────────────

resource topicBookingConfirmed 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: topicConfirmed
  properties: {
    defaultMessageTimeToLive: 'P14D'          // 14-day TTL
    maxSizeInMegabytes: 1024
    requiresDuplicateDetection: false
    enableBatchedOperations: true
    supportOrdering: false
  }
}

resource topicBookingCancelled 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: topicCancelled
  properties: {
    defaultMessageTimeToLive: 'P14D'
    maxSizeInMegabytes: 1024
    requiresDuplicateDetection: false
    enableBatchedOperations: true
    supportOrdering: false
  }
}

// ── Auth rule (least-privilege) ───────────────────────────────────────────────
// The API only needs to Send messages; consumers need Listen.
// One namespace-level rule with both rights avoids per-topic key management
// for this monolith setup.  In a microservices layout, create per-topic rules.

resource apiAuthRule 'Microsoft.ServiceBus/namespaces/authorizationRules@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'api-send-listen'
  properties: {
    rights: [
      'Send'
      'Listen'
    ]
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

@description('Service Bus namespace name.')
output namespaceName string = serviceBusNamespace.name

@secure()
@description('Primary connection string for the api-send-listen auth rule.')
output connectionString string = apiAuthRule.listKeys().primaryConnectionString
