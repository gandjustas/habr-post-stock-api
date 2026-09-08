import http from 'k6/http';

function rand(min, max) {
    return Math.round(Math.random() * (max - min) + min);
}

const itemsCount = 10;
const warehousesCount = 10;
export const options = {
    iterations: itemsCount * warehousesCount * 300,
    vus: 200,
};

export default function () {

    const url = __ENV.applicationUrl ?? 'http://localhost:5011';
    const params = {
        headers: {
            'Content-Type': 'application/json',
        },
    };

    const count = 1;
    let lines = [];
    for (let index = 0; index < count; index++) {
        lines.push({
            itemId: 1, //rand(1, warehousesCount),
            warehouseId: 1, //rand(1, warehousesCount),
            quantity: 1, //rand(1, 5)
        })
    }
    const payload = JSON.stringify({ id: crypto.randomUUID(), lines: lines });


    http.post(`${url}/place-order`, payload, params);
}