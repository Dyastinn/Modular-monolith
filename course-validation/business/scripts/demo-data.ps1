<#
.SYNOPSIS
    Fills a development ERP with a realistic month of business, through the real API, so every screen has
    something to show: orders in every status, discounts, shipments, invoices (paid, partly paid, overdue, void
    and reissued), payments with tax withheld, BIR Forms 2307, purchase orders in every status, stock moves.

.DESCRIPTION
    Run it once against a freshly created development database, with the app running:

        dotnet run --project src/Erp.Api          # in one terminal
        .\scripts\demo-data.ps1                   # in another, from course-validation\business

    Everything goes through the API as the person who would do it (rep, manager, clerk, buyer, accountant,
    admin), so every business rule applies and every number is consistent. The one exception: the API never
    lets an invoice be dated in the past, so for a meaningful aging report the script moves four invoices back
    in time with psql. Development databases only. Never run this against production.
#>
param(
    [string]$BaseUrl = 'http://localhost:5264',
    [string]$Database = 'erp',
    [string]$DbPassword = 'erp_dev_only',
    [string]$Psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
)

$ErrorActionPreference = 'Stop'
$Password = 'Password123!'
# The business's calendar date (Philippine time has no daylight saving).
$Today = [DateTime]::UtcNow.AddHours(8).Date
$TodayText = $Today.ToString('yyyy-MM-dd')

function Step([string]$text) { Write-Host "  $text" }
function Section([string]$text) { Write-Host ''; Write-Host $text -ForegroundColor Cyan }

function Invoke-Erp([string]$Method, [string]$Path, [string]$Token, $Body = $null) {
    $headers = @{}
    if ($Token) { $headers['Authorization'] = "Bearer $Token" }
    $params = @{ Method = $Method; Uri = "$BaseUrl/api$Path"; Headers = $headers; UseBasicParsing = $true }
    if ($null -ne $Body) {
        $params['ContentType'] = 'application/json; charset=utf-8'
        $params['Body'] = [Text.Encoding]::UTF8.GetBytes(($Body | ConvertTo-Json -Depth 6))
    }
    try { return Invoke-RestMethod @params }
    catch {
        $detail = $_.ErrorDetails.Message
        throw "$Method $Path failed: $($_.Exception.Message) $detail"
    }
}

function New-Erp([string]$Path, [string]$Token, $Body) { (Invoke-Erp 'POST' $Path $Token $Body).id }

# Sign-in is limited to 10 a minute per address (lesson 22); wait it out rather than fail.
function Get-Token([string]$Email) {
    for ($attempt = 1; ; $attempt++) {
        try { return (Invoke-Erp 'POST' '/auth/login' $null @{ email = $Email; password = $Password }).accessToken }
        catch {
            if ($attempt -ge 3 -or "$_" -notmatch '429') { throw }
            Write-Host '  (sign-in limit reached; waiting a minute)' -ForegroundColor DarkGray
            Start-Sleep -Seconds 61
        }
    }
}

# The outbox delivers a moment after commit: wait for what an event creates.
function Wait-For([scriptblock]$Read, [string]$What) {
    for ($i = 0; $i -lt 60; $i++) {
        $value = & $Read
        if ($value) { return $value }
        Start-Sleep -Milliseconds 500
    }
    throw "Timed out waiting for $What."
}

function Wait-PriceListItem([string]$Sku, [string]$Token) {
    Wait-For { (Invoke-Erp 'GET' "/price-list?search=$Sku" $Token).items | Select-Object -First 1 } "$Sku in the price list"
}

function Wait-Invoice([long]$OrderId, [string]$Token) {
    $number = (Invoke-Erp 'GET' "/sales-orders/$OrderId" $Token).orderNumber
    Wait-For {
        (Invoke-Erp 'GET' "/invoices?search=$number&pageSize=10" $Token).items |
            Where-Object { $_.orderNumber -eq $number -and $_.status -ne 'Void' } | Select-Object -First 1
    } "the invoice for $number"
}

function Line([long]$ItemId, [decimal]$Quantity) { @{ itemId = $ItemId; quantity = $Quantity } }

# Rep (or whoever) writes it; it's submitted unless told otherwise.
function New-Order([string]$Token, [long]$CustomerId, [object[]]$Lines, [switch]$Draft) {
    $id = New-Erp '/sales-orders' $Token @{ customerId = $CustomerId; lines = $Lines }
    if (-not $Draft) { Invoke-Erp 'POST' "/sales-orders/$id/submit" $Token | Out-Null }
    $id
}

function Ship([long]$OrderId) { New-Erp '/shipments' $script:clerk @{ salesOrderId = $OrderId } | Out-Null }

function Pay([long]$InvoiceId, [decimal]$Amount, [string]$Method, [string]$Reference, [decimal]$TaxWithheld = 0) {
    New-Erp "/invoices/$InvoiceId/payments" $script:accountant @{
        amount = $Amount; taxWithheld = $TaxWithheld; paidOn = $TodayText; method = $Method; reference = $Reference
    }
}

# ---------------------------------------------------------------------------------------------------------------
Write-Host "Loading demo data into $BaseUrl" -ForegroundColor Green
try { Invoke-RestMethod "$BaseUrl/health/ready" -UseBasicParsing | Out-Null }
catch { throw "The app isn't answering at $BaseUrl. Start it first: dotnet run --project src/Erp.Api" }

$manager = Get-Token 'manager@erp.local'
if ((Invoke-Erp 'GET' '/customers?search=Botika' $manager).totalCount -gt 0) {
    Write-Host "Demo data is already loaded. To load it again, recreate the database first." -ForegroundColor Yellow
    return
}
$rep = Get-Token 'rep@erp.local'
$admin = Get-Token 'admin@erp.local'
$clerk = Get-Token 'clerk@erp.local'
$buyer = Get-Token 'buyer@erp.local'
$accountant = Get-Token 'accountant@erp.local'

# ---------------------------------------------------------------------------------------------------------------
Section 'Company, tax and discounts'
$company = Invoke-Erp 'GET' '/company-profile' $accountant
Invoke-Erp 'PUT' '/company-profile' $accountant @{
    registeredName = $company.registeredName; tradeName = $company.tradeName; tin = $company.tin
    registeredAddress = $company.registeredAddress; taxRegime = $company.taxRegime; vatRate = $company.vatRate
    invoiceTitle = 'SALES INVOICE'; permitNumber = 'ACCN-DEMO-0001-2026'
    seriesRange = 'SI-2026-000001 to SI-2026-999999'; permitDateIssued = '01/05/2026'
} | Out-Null
Step 'BIR permit details filled in (demo values): they print at the bottom of every invoice'

# Demo values only: which programs apply, and at what rate, is the business's call with its tax adviser.
Invoke-Erp 'PUT' '/special-discount-programs/SP' $manager @{
    name = 'Solo parent'; idLabel = 'Solo Parent ID No.'; rate = 0.10; vatExempt = $true; isActive = $true
} | Out-Null
Step 'Special discount program added: Solo parent, 10%'

$pharmacy = New-Erp '/item-categories' $manager @{ name = 'Pharmacy'; maxQuantityPerOrder = 200; requiredRole = $null }
Step 'Item category added: Pharmacy'

# ---------------------------------------------------------------------------------------------------------------
Section 'Catalog, prices and opening stock'
$newItems = @(
    @{ sku = 'PHR-001'; name = 'Paracetamol 500mg (box of 100)'; unit = 'box'; fractions = $false; stock = 300
       price = 224.00; category = $pharmacy; tax = 'Vatable'; discount = $true },
    @{ sku = 'FOD-001'; name = 'Rice, well-milled (25 kg sack)'; unit = 'sack'; fractions = $false; stock = 80
       price = 1350.00; category = 1; tax = 'VatExempt'; discount = $false },
    @{ sku = 'EXP-001'; name = 'Export Crate, heat-treated'; unit = 'pc'; fractions = $false; stock = 150
       price = 1500.00; category = 1; tax = 'ZeroRated'; discount = $false },
    @{ sku = 'GEN-004'; name = 'PVC Pipe, 1/2 inch'; unit = 'm'; fractions = $true; stock = 600
       price = 48.75; category = 1; tax = 'Vatable'; discount = $false }
)
$itemIds = @{}
foreach ($i in $newItems) {
    $id = New-Erp '/items' $clerk @{ sku = $i.sku; name = $i.name; unit = $i.unit; allowsFractions = $i.fractions }
    Invoke-Erp 'POST' "/items/$id/adjustments" $clerk @{ quantity = $i.stock; reason = 'Opening count' } | Out-Null
    Wait-PriceListItem $i.sku $manager | Out-Null
    Invoke-Erp 'PUT' "/price-list/$id" $manager @{
        unitPrice = $i.price; categoryId = $i.category; isForSale = $true; taxType = $i.tax; specialDiscountEligible = $i.discount
    } | Out-Null
    $itemIds[$i.sku] = $id
    Step "$($i.name): $($i.stock) $($i.unit) on hand, PHP $($i.price), $($i.tax)$(if ($i.discount) { ', qualifies for SC/PWD' })"
}
$paracetamol = $itemIds['PHR-001']; $rice = $itemIds['FOD-001']; $crate = $itemIds['EXP-001']; $pipe = $itemIds['GEN-004']

$bracket = New-Erp '/items' $clerk @{ sku = 'OLD-001'; name = 'Steel Bracket (discontinued)'; unit = 'pc'; allowsFractions = $false }
Wait-PriceListItem 'OLD-001' $manager | Out-Null
Invoke-Erp 'POST' "/items/$bracket/deactivate" $clerk | Out-Null
Step 'Steel Bracket added, then deactivated: it shows as inactive and can''t be sold'

Invoke-Erp 'POST' '/items/2/adjustments' $clerk @{ quantity = -3; reason = 'Damaged in storage (water leak)' } | Out-Null
Step 'Gadget: 3 written off as damaged, with the reason in its movement history'

# ---------------------------------------------------------------------------------------------------------------
Section 'Purchasing'
$philPharma = New-Erp '/suppliers' $buyer @{ name = 'PhilPharma Distributors'; tin = '789-012-345-00000'; address = 'Mandaluyong City'
    email = 'orders@philpharma.test'; phone = '02 8765 4321'; paymentTermsDays = 30 }
$oldSupplier = New-Erp '/suppliers' $buyer @{ name = 'Old Supply Co.'; tin = $null; address = 'Caloocan City'
    email = $null; phone = $null; paymentTermsDays = 15 }
Invoke-Erp 'POST' "/suppliers/$oldSupplier/deactivate" $buyer | Out-Null
Step 'Suppliers: PhilPharma Distributors added; Old Supply Co. added and deactivated'

function New-PurchaseOrder([long]$Supplier, [object[]]$Lines, [string]$Notes) {
    New-Erp '/purchase-orders' $buyer @{ supplierId = $Supplier; expectedDate = $Today.AddDays(7).ToString('yyyy-MM-dd'); notes = $Notes; lines = $Lines }
}
function PoLine([long]$ItemId, [decimal]$Quantity, [decimal]$Cost) { @{ itemId = $ItemId; quantity = $Quantity; unitCost = $Cost } }
function Approve-AndSend([long]$Po) {
    Invoke-Erp 'POST' "/purchase-orders/$Po/approve" $admin | Out-Null
    Invoke-Erp 'POST' "/purchase-orders/$Po/send" $buyer | Out-Null
}
function Receive([long]$Po, [hashtable]$QuantityByItem) {
    $lines = (Invoke-Erp 'GET' "/purchase-orders/$Po" $clerk).lines |
        Where-Object { $QuantityByItem.ContainsKey("$($_.itemId)") } |
        ForEach-Object { @{ lineId = $_.id; quantity = $QuantityByItem["$($_.itemId)"] } }
    Invoke-Erp 'POST' "/purchase-orders/$Po/receipts" $clerk @{ lines = @($lines) } | Out-Null
}

$po1 = New-PurchaseOrder 1 @((PoLine 1 200 15), (PoLine 2 100 25)) 'Monthly restock'
Approve-AndSend $po1
Receive $po1 @{ '1' = 120; '2' = 100 }
Step 'PO to Manila Hardware: approved, sent, partly received (120 of 200 Widgets)'

$po2 = New-PurchaseOrder 2 @((PoLine 5 500 400)) 'Copper wire for Q4'
Approve-AndSend $po2
Receive $po2 @{ '5' = 500 }
Step 'PO to Cebu Metals: approved, sent, fully received (500 kg copper wire)'

$po3 = New-PurchaseOrder 1 @((PoLine $pipe 300 30)) 'Pipe order, supplier short'
Approve-AndSend $po3
Receive $po3 @{ "$pipe" = 180 }
Invoke-Erp 'POST' "/purchase-orders/$po3/close" $buyer | Out-Null
Step 'PO for PVC pipe: 180 of 300 m received, then closed (the rest isn''t coming)'

$po4 = New-PurchaseOrder $philPharma @((PoLine $paracetamol 100 150)) 'Awaiting approval'
Step 'PO to PhilPharma: left as a draft, waiting for an admin''s approval'

$po5 = New-PurchaseOrder $philPharma @((PoLine $paracetamol 50 155)) 'Duplicate of the draft'
Invoke-Erp 'POST' "/purchase-orders/$po5/cancel" $buyer | Out-Null
Step 'A duplicate PO, cancelled'

# ---------------------------------------------------------------------------------------------------------------
Section 'Customers'
function New-Customer([string]$Name, $Tin, [string]$Address, [decimal]$Limit, [int]$Terms) {
    New-Erp '/customers' $manager @{ name = $Name; tin = $Tin; address = $Address; creditLimit = $Limit; paymentTermsDays = $Terms }
}
$botika = New-Customer "Lola's Botika" $null 'Marikina City' 100000 0
$omega = New-Customer 'Omega Construction Corp.' '567-111-222-00000' 'Bonifacio Global City, Taguig' 2000000 30
$zeta = New-Customer 'Zeta Exports Inc.' '678-222-333-00000' 'Clark Freeport Zone, Pampanga' 500000 30
$theta = New-Customer 'Theta Hardware' '789-333-444-00000' 'Antipolo City' 50000 30
Step "Added Lola's Botika (cash, sells to seniors), Omega Construction (withholds tax), Zeta Exports (exports), Theta Hardware"

# ---------------------------------------------------------------------------------------------------------------
Section 'Sales orders, shipments and invoices'
$backdate = @{}   # invoice id -> days to move back, applied at the end

# 1. A senior citizen's discount, paid in cash, printed twice (the second copy says REPRINT).
$o = New-Order $rep $botika @((Line $paracetamol 10), (Line 1 2)) -Draft
Invoke-Erp 'PUT' "/sales-orders/$o/special-discount" $rep @{ programCode = 'SC'; holderName = 'Lola Basyang'; idNumber = 'OSCA-2026-0001' } | Out-Null
Invoke-Erp 'POST' "/sales-orders/$o/submit" $rep | Out-Null
Ship $o
$inv = Wait-Invoice $o $accountant
Pay $inv.id $inv.balanceDue 'Cash' 'Counter' | Out-Null
Invoke-WebRequest "$BaseUrl/api/invoices/$($inv.id)/pdf" -Headers @{ Authorization = "Bearer $accountant" } -UseBasicParsing | Out-Null
Invoke-WebRequest "$BaseUrl/api/invoices/$($inv.id)/pdf" -Headers @{ Authorization = "Bearer $accountant" } -UseBasicParsing | Out-Null
Step "$($inv.number): senior citizen discount (PHP 2,290 gross -> $($inv.total)), paid in cash, printed and reprinted"

# 2. Over PHP 50,000: waits for a manager, then ships; paid with 1% withheld, and the 2307 has arrived.
$o = New-Order $rep $omega @((Line 4 2))
Invoke-Erp 'POST' "/sales-orders/$o/approve" $manager | Out-Null
Ship $o
$inv = Wait-Invoice $o $accountant
$detail = Invoke-Erp 'GET' "/invoices/$($inv.id)" $accountant
$withheld = [math]::Round(($detail.total - $detail.vatAmount) * 0.01, 2)
Pay $inv.id ($detail.balanceDue - $withheld) 'BankTransfer' 'BDO-778812' $withheld | Out-Null
New-Erp '/withholding-certificates' $accountant @{ customerId = $omega; year = $Today.Year; quarter = [math]::Ceiling($Today.Month / 3)
    amount = $withheld; reference = 'Omega 2307 Q' + [math]::Ceiling($Today.Month / 3); receivedOn = $TodayText } | Out-Null
Step "$($inv.number): 2 Server Racks, approved by the manager, paid by transfer with PHP $withheld withheld; Form 2307 received"

# 3. Mixed VAT treatments (VATable, zero-rated, VAT-exempt); partly paid with tax withheld, certificate still missing.
$o = New-Order $rep $zeta @((Line $crate 20), (Line $rice 5), (Line 1 10))
Ship $o
$inv = Wait-Invoice $o $accountant
Pay $inv.id 20000 'Check' 'MBTC-004512' 200 | Out-Null
Step "$($inv.number): export crates (zero-rated), rice (VAT-exempt) and widgets; PHP 20,000 paid + PHP 200 withheld, 2307 missing"

# 4. Billed wrong: voided with a reason, then reissued under a new number for the same shipment.
$o = New-Order $rep $zeta @((Line $pipe 25.5))
Ship $o
$inv = Wait-Invoice $o $accountant
Invoke-Erp 'POST' "/invoices/$($inv.id)/void" $accountant @{ reason = 'Billed to the wrong branch address' } | Out-Null
$reissued = New-Erp "/invoices/$($inv.id)/reissue" $accountant @{}
Step "$($inv.number): voided (wrong address) and reissued for the same shipment"

# 5. Controlled category: only a sales manager may sell the Restricted Kit.
$o = New-Order $manager 4 @((Line 3 2))
Ship $o
$inv = Wait-Invoice $o $accountant
Pay $inv.id $inv.balanceDue 'BankTransfer' 'BPI-223344' | Out-Null
Step "$($inv.number): 2 Restricted Kits sold by the manager (controlled category), paid in full"

# 6. Overdue more than 90 days, and the customer is now blocked.
$o = New-Order $rep $theta @((Line 1 40), (Line 2 10))
Ship $o
$inv = Wait-Invoice $o $accountant
$backdate[$inv.id] = 30 + 100
Invoke-Erp 'POST' "/customers/$theta/block" $manager | Out-Null
Step "$($inv.number): Theta Hardware, unpaid and 100 days overdue; Theta is blocked from new orders"

# 7. 45 days overdue, unpaid.
$o = New-Order $rep 4 @((Line 2 20))
Ship $o
$inv = Wait-Invoice $o $accountant
$backdate[$inv.id] = 45 + 45
Step "$($inv.number): Delta Holdings, unpaid and 45 days overdue"

# 8. 20 days overdue, partly paid; a second payment's check bounced and was voided.
$o = New-Order $rep 5 @((Line 5 5))
Ship $o
$inv = Wait-Invoice $o $accountant
Pay $inv.id 1000 'Check' 'SBC-100045' | Out-Null
$bounced = Pay $inv.id 500 'Check' 'SBC-100046'
Invoke-Erp 'POST' "/invoices/$($inv.id)/payments/$bounced/void" $accountant @{ reason = 'Check bounced (insufficient funds)' } | Out-Null
$backdate[$inv.id] = 60 + 20
Step "$($inv.number): Epsilon Retail, PHP 1,000 paid, a PHP 500 check bounced and voided, 20 days overdue"

# 9. Not yet due, unpaid.
$o = New-Order $rep $zeta @((Line $crate 10))
Ship $o
$inv = Wait-Invoice $o $accountant
Step "$($inv.number): Zeta Exports, unpaid, not yet due"

# Orders that stop before shipping, one per status.
$o = New-Order $rep 5 @((Line 4 1))
Step 'Epsilon Retail: Server Rack order waiting for a manager''s approval (over PHP 50,000)'

$o = New-Order $rep 4 @((Line 4 1), (Line 5 50))
Invoke-Erp 'POST' "/sales-orders/$o/reject" $manager @{ reason = 'Customer asked to wait for next quarter''s budget' } | Out-Null
Step 'Delta Holdings: large order rejected by the manager, with a reason'

$o = New-Order $rep 1 @((Line 2 3), (Line 5 2.5))
Step 'Acme Corp: approved automatically, waiting to be shipped'

$o = New-Order $rep 1 @((Line 1 10))
Invoke-Erp 'POST' "/sales-orders/$o/cancel" $rep | Out-Null
Step 'Acme Corp: approved, then cancelled (reserved stock released, charge reversed)'

$o = New-Order $rep 1 @((Line 2 5)) -Draft
Step 'Acme Corp: a draft, not yet submitted'

$o = New-Order $rep $botika @((Line $paracetamol 4)) -Draft
Invoke-Erp 'PUT' "/sales-orders/$o/special-discount" $rep @{ programCode = 'PWD'; holderName = 'Juan dela Cruz'; idNumber = 'PWD-NCR-2026-0042' } | Out-Null
Step "Lola's Botika: a draft with a PWD discount applied"

# ---------------------------------------------------------------------------------------------------------------
Section 'People'
$temp = New-Erp '/users' $admin @{ email = 'rep2@erp.local'; fullName = 'Paolo Garcia'; role = 'SalesRep'; temporaryPassword = 'Welcome-2026-Temp!' }
Invoke-Erp 'POST' "/users/$temp/disable" $admin | Out-Null
Step 'Paolo Garcia (rep2@erp.local): created, then disabled'
try { Invoke-Erp 'POST' '/auth/login' $null @{ email = 'rep@erp.local'; password = 'wrong-password' } | Out-Null } catch { }
Step 'One failed sign-in for rep@erp.local, for the sign-in log'

# ---------------------------------------------------------------------------------------------------------------
Section 'Aging: moving four invoices back in time'
if (Test-Path $Psql) {
    $sql = ($backdate.GetEnumerator() | ForEach-Object {
        "UPDATE invoicing.invoice SET issue_date = issue_date - $($_.Value), due_date = due_date - $($_.Value) WHERE id = $($_.Key);"
    }) -join ' '
    $env:PGPASSWORD = $DbPassword
    & $Psql -h localhost -U postgres -d $Database -q -c $sql
    if ($LASTEXITCODE -ne 0) { throw 'psql failed while backdating invoices.' }
    Step 'Done: the receivables aging report now has amounts in 1-30, 31-60 and over 90 days'
} else {
    Write-Host "  psql not found at $Psql; skipped. Every invoice stays 'not yet due'." -ForegroundColor Yellow
}

Write-Host ''
Write-Host 'Demo data loaded. Sign in at the app with any demo account (password Password123!):' -ForegroundColor Green
Write-Host '  rep@erp.local, manager@erp.local, clerk@erp.local, buyer@erp.local, accountant@erp.local, admin@erp.local'
