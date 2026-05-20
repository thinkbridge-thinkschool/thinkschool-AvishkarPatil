async function test() {
  console.log("POST /api/quotes");
  let res = await fetch("http://localhost:3000/api/quotes", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ author: "Grace Hopper", text: "A ship in port is safe, but that's not what ships are built for." })
  });
  console.log(res.status, await res.text());

  console.log("\nGET /api/quotes");
  res = await fetch("http://localhost:3000/api/quotes");
  console.log(res.status, await res.text());

  console.log("\nGET /api/quotes/1");
  res = await fetch("http://localhost:3000/api/quotes/1");
  console.log(res.status, await res.text());

  console.log("\nDELETE /api/quotes/1");
  res = await fetch("http://localhost:3000/api/quotes/1", { method: "DELETE" });
  console.log(res.status);

  console.log("\nGET /api/quotes/1");
  res = await fetch("http://localhost:3000/api/quotes/1");
  console.log(res.status);
}

test().catch(console.error);
