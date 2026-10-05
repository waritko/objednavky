# Restaurant ordering app
 - Web application
 - Used by waiters when customer orders and when they pay
 - Used by kitchen to see what they need to prepare
 - Whole app is in Czech language

## Technology used
 - Backend in C#
 - Frontend in React
 - Data stored in SQLite with preparation for SQL Server
    - Prepare tests for both
 - CI done via TeamCity

## UX
 - Web must be well-usable on standard mobile phone
 - Optimize for touch devices, no excesive use of keyboard for day-to-day use

## App
### Ordering
 - Order is tied to a table
    - When table has an active order, allow adding to it
    - When no active order, create a new one
 - All orderable items are put into categories and sub-categories
 - When ordering multiple of the same item, multiple clicks on item are used
 - Show list of ordered items while ordering

### Viewing orders
 - For active order list, allow filtering
   - Not processed (Nevydané)
   - Not paid (Nezaplacené)
 - Allow changing order contents
    - Newly added lines are marked as Not Processed
 - Allow marking order lines as Processed and Paid
 - Allow marking entire order (all lines) as Processed or Paid

 ### Administration
  - Allow administering of available tables
  - Allow administering of items for order
  - Allow import of orderable items via CSV

### Data
 - Each orderable item has following properties
   - Category / subcategory
   - Name
   - Price
 - Historical order data are archived indeffinetelly