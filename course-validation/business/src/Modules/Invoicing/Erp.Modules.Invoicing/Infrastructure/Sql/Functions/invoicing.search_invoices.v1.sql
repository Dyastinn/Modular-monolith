-- Released scripts are never edited: change a function with a .v2 file and a new migration.
-- p_overdue_on set: only open invoices whose due date is before that day.
CREATE OR REPLACE FUNCTION invoicing.search_invoices(p_customer_id bigint, p_status integer, p_search text,
                                                     p_overdue_on date, p_page integer, p_page_size integer)
RETURNS TABLE (id bigint, number text, customer_id bigint, customer_name text, order_number text, issue_date date,
               due_date date, status integer, total numeric, amount_paid numeric)
LANGUAGE sql STABLE
AS $$
    SELECT i.id, i.number::text, i.customer_id, i.customer_name::text, i.order_number::text, i.issue_date, i.due_date,
           i.status, i.total, i.amount_paid
    FROM invoicing.invoice i
    WHERE (p_customer_id IS NULL OR i.customer_id = p_customer_id)
      AND (p_status      IS NULL OR i.status      = p_status)
      AND (p_search      IS NULL OR i.number ILIKE '%' || p_search || '%' OR i.customer_name ILIKE '%' || p_search || '%'
                                 OR i.order_number ILIKE '%' || p_search || '%')
      AND (p_overdue_on  IS NULL OR (i.status IN (0, 1) AND i.due_date < p_overdue_on))
    ORDER BY i.issue_date DESC, i.id DESC
    OFFSET (p_page - 1) * p_page_size
    LIMIT p_page_size;
$$;

CREATE OR REPLACE FUNCTION invoicing.count_invoices(p_customer_id bigint, p_status integer, p_search text, p_overdue_on date)
RETURNS bigint
LANGUAGE sql STABLE
AS $$
    SELECT COUNT(*) FROM invoicing.invoice i
    WHERE (p_customer_id IS NULL OR i.customer_id = p_customer_id)
      AND (p_status      IS NULL OR i.status      = p_status)
      AND (p_search      IS NULL OR i.number ILIKE '%' || p_search || '%' OR i.customer_name ILIKE '%' || p_search || '%'
                                 OR i.order_number ILIKE '%' || p_search || '%')
      AND (p_overdue_on  IS NULL OR (i.status IN (0, 1) AND i.due_date < p_overdue_on));
$$;

CREATE OR REPLACE FUNCTION invoicing.get_invoice(p_id bigint)
RETURNS TABLE (id bigint, number text, shipment_id bigint, sales_order_id bigint, order_number text, customer_id bigint,
               customer_name text, customer_tin text, customer_address text, issue_date date, due_date date,
               status integer, total numeric, amount_paid numeric, void_reason text, voided_by bigint, voided_utc timestamptz,
               tax_regime integer, vat_rate numeric, vatable_sales numeric, vat_amount numeric, zero_rated_sales numeric,
               vat_exempt_sales numeric, print_count integer, special_discount_program text, special_discount_id_label text,
               special_discount_holder_name text, special_discount_id_number text, special_discount_rate numeric,
               special_discount_vat_rate_removed numeric, special_discount_vat_exemption numeric, special_discount_amount numeric,
               seller_name text, seller_trade_name text, seller_tin text, seller_address text, seller_invoice_title text,
               seller_permit_number text, seller_series_range text, seller_permit_date_issued text)
LANGUAGE sql STABLE
AS $$
    SELECT i.id, i.number::text, i.shipment_id, i.sales_order_id, i.order_number::text, i.customer_id,
           i.customer_name::text, i.customer_tin::text, i.customer_address::text, i.issue_date, i.due_date,
           i.status, i.total, i.amount_paid, i.void_reason::text, i.voided_by, i.voided_utc,
           i.tax_regime, i.vat_rate, i.vatable_sales, i.vat_amount, i.zero_rated_sales, i.vat_exempt_sales, i.print_count,
           i.special_discount_program::text, i.special_discount_id_label::text, i.special_discount_holder_name::text,
           i.special_discount_id_number::text, i.special_discount_rate, i.special_discount_vat_rate_removed,
           i.special_discount_vat_exemption, i.special_discount_amount,
           i.seller_name::text, i.seller_trade_name::text, i.seller_tin::text, i.seller_address::text,
           i.seller_invoice_title::text, i.seller_permit_number::text, i.seller_series_range::text,
           i.seller_permit_date_issued::text
    FROM invoicing.invoice i
    WHERE i.id = p_id;
$$;

CREATE OR REPLACE FUNCTION invoicing.get_invoice_lines(p_id bigint)
RETURNS TABLE (description text, unit text, quantity numeric, unit_price numeric, tax_type integer,
               special_discount_eligible boolean)
LANGUAGE sql STABLE
AS $$
    SELECT l.description::text, l.unit::text, l.quantity, l.unit_price, l.tax_type, l.special_discount_eligible
    FROM invoicing.invoice_line l WHERE l.invoice_id = p_id ORDER BY l.id;
$$;

CREATE OR REPLACE FUNCTION invoicing.get_invoice_payments(p_id bigint)
RETURNS TABLE (id bigint, amount numeric, tax_withheld numeric, paid_on date, method integer, reference text, recorded_by bigint,
               recorded_utc timestamptz, voided_utc timestamptz, void_reason text)
LANGUAGE sql STABLE
AS $$
    SELECT p.id, p.amount, p.tax_withheld, p.paid_on, p.method, p.reference::text, p.recorded_by, p.recorded_utc, p.voided_utc, p.void_reason::text
    FROM invoicing.payment p WHERE p.invoice_id = p_id ORDER BY p.paid_on, p.id;
$$;
