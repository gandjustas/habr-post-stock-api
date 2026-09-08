TRUNCATE TABLE orders CASCADE;
TRUNCATE TABLE stock;
TRUNCATE TABLE operations;
INSERT INTO stock 
select *, random(100000, 1000000) as quantity from generate_series(1,10) item_id, generate_series(1,10) warehouse_id;